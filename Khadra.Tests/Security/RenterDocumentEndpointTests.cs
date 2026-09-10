using System.Net;
using System.Reflection;
using CSharpFunctionalExtensions;
using Khadra.Application.Bookings.Dtos;
using Khadra.Application.Bookings.RenterDocuments;
using Khadra.Application.Common;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
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
/// The transport side of a gallery reading a renter's licence (pre-launch item 63).
/// </summary>
/// <remarks>
/// The authorization RULE is proven in <c>RenterDocumentAccessTests</c>, against the handler. What is
/// left is the layer above it, where a mistake is a missing attribute rather than a missing branch —
/// the failure mode nothing else in the suite would notice — and the response headers, where a
/// mistake leaves a passport in a shared proxy.
///
/// The live-token cases (a customer's own token answering 403 here) are not reachable in this suite:
/// <c>OnTokenValidated</c> re-reads the account to check its security stamp, so a real bearer needs a
/// database. The policy assertions below pin the same thing one layer down, and the walkthrough in
/// <c>docs/workflows.md</c> covers it against a live server.
/// </remarks>
public sealed class RenterDocumentEndpointTests : IDisposable
{
    private static readonly Guid BookingId = Guid.CreateVersion7();
    private static readonly Guid DocumentId = Guid.CreateVersion7();

    private readonly WebApplicationFactory<Khadra.WebAPI.WebApiAssemblyMarker> _factory =
        new WebApplicationFactory<Khadra.WebAPI.WebApiAssemblyMarker>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=khadra_tests;Username=x;Password=y");
                builder.UseSetting("Authentication:Jwt:SigningKey", new string('k', 48));
                builder.UseSetting("Database:AutoMigrate", "false");
                builder.UseSetting("Email:Provider", "Logging");
                builder.UseSetting("KnownProxies:0", "10.255.255.1");
            });

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Listing_a_renters_documents_without_a_token_is_401()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(
            new Uri($"/api/v1/bookings/{BookingId}/renter-documents", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Opening_a_renters_document_without_a_token_is_401()
    {
        // Authorization runs before the handler, so this never reaches a query: an anonymous caller
        // learns nothing about whether the booking or the document is real.
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(
            new Uri($"/api/v1/bookings/{BookingId}/renter-documents/{DocumentId}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// The controller is dealer staff only, and the whole feature rests on that one attribute.
    /// </summary>
    /// <remarks>
    /// Asserted by reflection because the alternative — a real customer bearer token — needs a
    /// database this suite does not have. Deliberately checks <c>DealerStaff</c> and NOT
    /// <c>ApprovedDealerStaff</c>: the trading gate belongs on approve and reject, and putting it
    /// here would refuse the licence to a suspended gallery that is still allowed to hand the car
    /// over. See <c>RenterDocumentAccessTests</c> for the same rule stated as behaviour.
    /// </remarks>
    [Fact]
    public void The_controller_admits_dealer_staff_only()
    {
        var authorize = typeof(RenterDocumentsController)
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .SingleOrDefault();

        Assert.NotNull(authorize);
        Assert.Equal(SecurityPolicies.DealerStaff, authorize.Policy);
    }

    /// <summary>
    /// It does not inherit the auth limiter, which on a GET is ten requests a minute per address.
    /// </summary>
    /// <remarks>
    /// <c>CredentialSubject</c> only names a subject for a POST body under <c>/api/v1/auth</c>, so
    /// <c>RateLimitPolicies.Auth</c> silently degrades to an address-only bucket of ten — and one
    /// gallery's office NAT is one address, while a handover screen opens three or four documents at
    /// once. Its own policy is address-keyed too (the limiter runs before authentication, so no
    /// policy on this API can key on the account), but with a ceiling twelve times higher and a
    /// queue, because an <c>&lt;img&gt;</c> cannot act on a 429.
    /// </remarks>
    [Fact]
    public void It_does_not_inherit_the_auth_limiter()
    {
        var limiter = typeof(RenterDocumentsController)
            .GetCustomAttributes<EnableRateLimitingAttribute>(inherit: true)
            .SingleOrDefault();

        Assert.NotNull(limiter);
        Assert.Equal(RateLimitPolicies.PrivateDocuments, limiter.PolicyName);
    }

    // ── The response, once a caller has earned it ───────────────────────────────────────────────

    [Fact]
    public async Task A_served_document_is_private_and_never_cached()
    {
        // The one header that matters on a body like this: an identity document must not settle into
        // a shared proxy or the browser's disk cache. Driven through the real controller so the
        // shared delivery path is what is being measured.
        var controller = ControllerWith(Result.Success<OpenedDocument, Error>(
            new OpenedDocument(new MemoryStream([1, 2, 3]), "image/jpeg")));

        var result = await controller.Open(BookingId, DocumentId, default);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("image/jpeg", file.ContentType);
        Assert.Equal("no-store, private", controller.Response.Headers.CacheControl.ToString());
        Assert.Equal("nosniff", controller.Response.Headers["X-Content-Type-Options"].ToString());
        // Never an attachment name: a stored key is a generated guid, and the customer's own file
        // name is a string they chose and not a gallery's to receive. Empty rather than null is how
        // ASP.NET spells "no Content-Disposition".
        Assert.True(string.IsNullOrEmpty(file.FileDownloadName));
    }

    [Fact]
    public async Task A_booking_that_is_no_longer_live_answers_409_with_a_code_the_console_can_read()
    {
        var controller = ControllerWith(
            Result.Failure<OpenedDocument, Error>(BookingErrors.RenterDocumentsNotAvailable));

        var result = await controller.Open(BookingId, DocumentId, default);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        var details = Assert.IsType<ProblemDetails>(problem.Value);
        Assert.Equal("booking.renter_documents_not_available", details.Extensions["code"]);
    }

    [Fact]
    public async Task A_booking_belonging_to_another_dealership_answers_404_and_says_nothing_more()
    {
        // The whole point of the 404: the body must not distinguish "not yours" from "no such
        // booking", or one gallery can enumerate another's.
        var controller = ControllerWith(Result.Failure<OpenedDocument, Error>(BookingErrors.NotFound));

        var result = await controller.Open(BookingId, DocumentId, default);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("booking.not_found", Assert.IsType<ProblemDetails>(problem.Value).Extensions["code"]);
    }

    [Fact]
    public async Task The_listing_that_reaches_the_browser_carries_no_key_and_no_address()
    {
        // Belt and braces over the DTO's own shape: whatever is added to it later, nothing that looks
        // like a bucket path or a URL may reach a gallery's browser.
        var listing = new RenterDocumentsDto(
            [new RenterDocumentDto(DocumentId, "DrivingLicenceFront", "PendingReview", "image/jpeg", DateTimeOffset.UtcNow)],
            IsComplete: false,
            ["NationalId"]);
        var controller = ControllerWith(Result.Success<RenterDocumentsDto, Error>(listing));

        var result = await controller.List(BookingId, default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var rendered = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.DoesNotContain("http", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("supabase", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("customers/", rendered, StringComparison.OrdinalIgnoreCase);
        // The listing is private data too: which papers a named person holds, and when they filed
        // them. It went out with no Cache-Control at all until a live walkthrough looked at the
        // headers rather than the body.
        Assert.Equal("no-store, private", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task A_refusal_to_list_is_not_cached_either()
    {
        // A cached 409 would go on telling a gallery their window has closed after the booking was
        // reinstated, and there would be no request for the server to correct it on.
        var controller = ControllerWith(
            Result.Failure<RenterDocumentsDto, Error>(BookingErrors.RenterDocumentsNotAvailable));

        await controller.List(BookingId, default);

        Assert.Equal("no-store, private", controller.Response.Headers.CacheControl.ToString());
    }

    /// <summary>The real controller over a substituted mediator, with a live response to write headers to.</summary>
    private static RenterDocumentsController ControllerWith<T>(Result<T, Error> answer)
    {
        var mediator = Substitute.For<ISender>();
        mediator
            .Send(Arg.Any<IRequest<Result<T, Error>>>(), Arg.Any<CancellationToken>())
            .Returns(answer);

        var actor = Substitute.For<ICurrentActor>();
        actor.UserId.Returns(Id.New());

        var services = new ServiceCollection();
        services.AddSingleton(mediator);

        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        return new RenterDocumentsController(actor)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
    }
}
