using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Khadra.Tests.Support;
using Khadra.WebAPI;
using Khadra.WebAPI.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Khadra.Tests.Security;

/// <summary>
/// The office payables ledger's endpoints (payments Phase 8): the administrator's behind the administrator policy,
/// every one of them, the office's behind dealer staff (the reports grant is the handler's), no anonymous action
/// anywhere — and an anonymous caller learns nothing, refusals included kept out of caches.
/// </summary>
public sealed class PayablesEndpointTests : IDisposable
{
    private readonly WebApplicationFactory<WebApiAssemblyMarker> _factory =
        new WebApplicationFactory<WebApiAssemblyMarker>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.IsolateFromDeveloperDatabase();
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=khadra_tests;Username=x;Password=y");
                builder.UseSetting("Authentication:Jwt:SigningKey", new string('k', 48));
                builder.UseSetting("Database:AutoMigrate", "false");
                builder.UseSetting("Email:Provider", "Logging");
                builder.UseSetting("KnownProxies:0", "10.255.255.1");
            });

    public void Dispose() => _factory.Dispose();

    [Theory]
    [InlineData("/api/v1/admin/office-balances")]
    [InlineData("/api/v1/admin/office-payables")]
    [InlineData("/api/v1/admin/office-payables/holds")]
    [InlineData("/api/v1/admin/offices/01a0d99c-8f0d-7092-8ec4-4176ed842a69/settlements")]
    [InlineData("/api/v1/admin/office-settlements/01a0d99c-8f0d-7092-8ec4-4176ed842a69")]
    [InlineData("/api/v1/admin/finance/summary")]
    [InlineData("/api/v1/dealers/me/payouts")]
    [InlineData("/api/v1/dealers/me/payouts/payables")]
    [InlineData("/api/v1/dealers/me/payouts/settlements")]
    [InlineData("/api/v1/dealers/me/payouts/settlements/01a0d99c-8f0d-7092-8ec4-4176ed842a69")]
    public async Task Reading_an_offices_money_without_a_token_is_401(string path)
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/admin/offices/01a0d99c-8f0d-7092-8ec4-4176ed842a69/settlements")]
    [InlineData("/api/v1/admin/office-settlements/01a0d99c-8f0d-7092-8ec4-4176ed842a69/void")]
    [InlineData("/api/v1/admin/office-payables/01a0d99c-8f0d-7092-8ec4-4176ed842a69/hold")]
    [InlineData("/api/v1/admin/office-payables/01a0d99c-8f0d-7092-8ec4-4176ed842a69/release")]
    public async Task Recording_money_without_a_token_is_401(string path)
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync(new Uri(path, UriKind.Relative), new { reason = "x" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(typeof(AdminPayablesController), SecurityPolicies.Admin)]
    [InlineData(typeof(DealerPayoutsController), SecurityPolicies.DealerStaff)]
    public void Every_action_sits_behind_its_policy_and_none_is_anonymous(Type controller, string policy)
    {
        Assert.Equal(policy, controller.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
        foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public void The_office_never_gets_an_action_that_changes_the_ledger()
    {
        var verbs = typeof(DealerPayoutsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(action => action.GetCustomAttributes<HttpMethodAttribute>())
            .SelectMany(attribute => attribute.HttpMethods)
            .Distinct()
            .ToList();

        Assert.Equal(["GET"], verbs);
    }
}
