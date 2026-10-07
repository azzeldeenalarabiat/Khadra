using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using CSharpFunctionalExtensions;
using Khadra.Application.Bookings.RenterDocuments;
using Khadra.Application.IdentityAccess.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Tests.Support;
using Khadra.WebAPI;
using Khadra.WebAPI.Controllers;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Khadra.Tests.Security;

/// <summary>
/// The transport side of an administrator opening and rejecting a renter's document (Wave 4, W4-9; checklist 27).
/// </summary>
/// <remarks>
/// The rule is proven against the handlers in <c>AdminCustomerDocumentTests</c>. What is left is the layer above them,
/// where a mistake is a missing attribute — the Admin policy, the private-documents limiter — and the response
/// headers, where a mistake leaves a passport in a shared proxy. As in <c>RenterDocumentEndpointTests</c>, a live
/// bearer needs a database this suite does not have, so the policy is pinned by reflection.
/// </remarks>
public sealed class AdminCustomerDocumentEndpointTests : IDisposable
{
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly Guid DocumentId = Guid.CreateVersion7();

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

    [Fact]
    public async Task Opening_or_rejecting_without_a_token_is_401()
    {
        using var client = _factory.CreateClient();

        using var open = await client.GetAsync(
            new Uri($"/api/v1/admin/customers/{UserId}/documents/{DocumentId}", UriKind.Relative));
        using var reject = await client.PostAsJsonAsync(
            new Uri($"/api/v1/admin/customers/{UserId}/documents/{DocumentId}/reject", UriKind.Relative),
            new { reason = "Blurred.", uploadedAt = DateTimeOffset.UtcNow });

        Assert.Equal(HttpStatusCode.Unauthorized, open.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, reject.StatusCode);
    }

    /// <summary>Administrators only, from the class, and neither action loosens it.</summary>
    [Fact]
    public void Both_routes_are_for_administrators_only()
    {
        var controller = typeof(AdminCustomersController);
        Assert.Equal(SecurityPolicies.Admin, controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Single().Policy);

        foreach (var name in new[] { nameof(AdminCustomersController.OpenDocument), nameof(AdminCustomersController.RejectDocument) })
        {
            var action = controller.GetMethod(name)!;
            Assert.Empty(action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
            Assert.Empty(action.GetCustomAttributes<AuthorizeAttribute>(inherit: true));
        }
    }

    /// <summary>The file route takes the private-documents limiter, not the auth limiter a GET would otherwise fall to.</summary>
    [Fact]
    public void Opening_a_file_takes_the_private_documents_limiter()
    {
        var limiter = typeof(AdminCustomersController)
            .GetMethod(nameof(AdminCustomersController.OpenDocument))!
            .GetCustomAttributes<EnableRateLimitingAttribute>(inherit: true)
            .SingleOrDefault();

        Assert.NotNull(limiter);
        Assert.Equal(RateLimitPolicies.PrivateDocuments, limiter.PolicyName);
    }

    [Fact]
    public async Task A_served_document_is_private_and_never_cached()
    {
        var controller = ControllerWith(Result.Success<OpenedDocument, Error>(new OpenedDocument(new MemoryStream([1, 2, 3]), "application/pdf")));

        var result = await controller.OpenDocument(UserId, DocumentId, default);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("no-store, private", controller.Response.Headers.CacheControl.ToString());
        Assert.Equal("nosniff", controller.Response.Headers["X-Content-Type-Options"].ToString());
        Assert.True(string.IsNullOrEmpty(file.FileDownloadName));
    }

    [Fact]
    public async Task A_file_replaced_since_it_was_opened_answers_409_with_a_code_the_console_can_read()
    {
        var controller = ControllerWith(Result.Failure<CustomerProfile, Error>(IdentityErrors.DocumentChangedSinceViewed));

        var result = await controller.RejectDocument(
            UserId, DocumentId, new AdminCustomersController.RejectDocumentRequest("Blurred.", DateTimeOffset.UtcNow), default);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Equal("documents.changed_since_viewed", Assert.IsType<ProblemDetails>(problem.Value).Extensions["code"]);
    }

    /// <summary>
    /// A rejection carries what the administrator decided and which upload they judged — nothing that names who they
    /// are, which comes from the validated token.
    /// </summary>
    [Fact]
    public void A_rejection_carries_a_reason_and_the_upload_it_judged_and_nothing_else()
    {
        var parameters = typeof(AdminCustomersController.RejectDocumentRequest).GetConstructors().Single().GetParameters();

        Assert.Equal(["Reason", "UploadedAt"], parameters.Select(parameter => parameter.Name));
        Assert.All(parameters, parameter =>
            Assert.NotNull(parameter.GetCustomAttribute<System.ComponentModel.DataAnnotations.RequiredAttribute>()));
    }

    /// <summary>The real controller over a substituted mediator, with a live response to write headers to.</summary>
    private static AdminCustomersController ControllerWith<T>(Result<T, Error> answer)
    {
        var mediator = Substitute.For<ISender>();
        mediator
            .Send(Arg.Any<IRequest<Result<T, Error>>>(), Arg.Any<CancellationToken>())
            .Returns(answer);

        var services = new ServiceCollection();
        services.AddSingleton(mediator);

        return new AdminCustomersController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() },
            },
        };
    }
}
