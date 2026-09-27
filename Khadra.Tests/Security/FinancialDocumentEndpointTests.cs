using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Khadra.Tests.Support;
using Khadra.WebAPI;
using Khadra.WebAPI.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Khadra.Tests.Security;

/// <summary>
/// The transport side of issued financial documents (payments Phase 5). Who reads which document is proven
/// against the handlers (<c>FinancialDocumentIssuanceTests</c>); what is left is the layer above them, where a
/// mistake is a missing attribute: an anonymous caller learns nothing, the customer's endpoints need a
/// customer, and the administrator's sit behind the administrator policy.
/// </summary>
public sealed class FinancialDocumentEndpointTests : IDisposable
{
    private static readonly Guid SomeId = Guid.CreateVersion7();

    private readonly WebApplicationFactory<WebApiAssemblyMarker> _factory =
        new WebApplicationFactory<WebApiAssemblyMarker>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.IsolateFromDeveloperDatabase();
                builder.UseSetting("Authentication:Jwt:SigningKey", new string('k', 48));
                builder.UseSetting("Email:Provider", "Logging");
                builder.UseSetting("KnownProxies:0", "10.255.255.1");
            });

    public void Dispose() => _factory.Dispose();

    [Theory]
    [InlineData("GET", "/api/v1/customers/me/financial-documents")]
    [InlineData("GET", "/api/v1/financial-documents/{0}")]
    [InlineData("GET", "/api/v1/bookings/{0}/financial-documents")]
    [InlineData("GET", "/api/v1/admin/financial-documents")]
    [InlineData("GET", "/api/v1/admin/financial-documents/{0}")]
    [InlineData("GET", "/api/v1/admin/financial-documents/holds")]
    [InlineData("GET", "/api/v1/admin/financial-documents/vocabulary")]
    [InlineData("GET", "/api/v1/admin/bookings/{0}/financial-documents")]
    [InlineData("POST", "/api/v1/admin/financial-documents/{0}/void")]
    public async Task Every_financial_document_endpoint_needs_a_token(string method, string template)
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(string.Format(CultureInfo.InvariantCulture, template, SomeId), UriKind.Relative));
        if (method == "POST")
            request.Content = JsonContent.Create(new { reason = "Wrong." });

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void The_customers_documents_need_a_customer()
    {
        Assert.Equal(SecurityPolicies.Customer, typeof(FinancialDocumentsController).GetCustomAttribute<AuthorizeAttribute>()?.Policy);
        foreach (var action in typeof(FinancialDocumentsController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public void The_administrators_documents_and_the_void_sit_behind_the_administrator_policy()
    {
        Assert.Equal(SecurityPolicies.Admin, typeof(AdminFinancialDocumentsController).GetCustomAttribute<AuthorizeAttribute>()?.Policy);
        foreach (var action in typeof(AdminFinancialDocumentsController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
            // No action-level policy may loosen the class's.
            Assert.Null(action.GetCustomAttribute<AuthorizeAttribute>());
        }

        var voiding = typeof(AdminFinancialDocumentsController).GetMethod(nameof(AdminFinancialDocumentsController.Void))!;
        Assert.Equal("financial-documents/{documentId:guid}/void", voiding.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Fact]
    public void A_bookings_documents_are_read_by_its_parties_and_the_handler_decides_which()
    {
        var action = typeof(BookingsController).GetMethod(nameof(BookingsController.GetFinancialDocuments))!;

        Assert.NotNull(typeof(BookingsController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
        // No role policy on the action: customers and office staff both reach it; the office gets an empty list.
        Assert.Null(action.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal("{bookingId:guid}/financial-documents", action.GetCustomAttribute<HttpGetAttribute>()!.Template);
    }
}
