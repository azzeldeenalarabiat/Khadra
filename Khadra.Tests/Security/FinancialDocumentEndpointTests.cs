using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.FinancialDocuments.Queries;
using Khadra.Application.FinancialDocuments.VoidFinancialDocument;
using Khadra.Application.Payments.Financials;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.FinancialDocuments;
using Khadra.Tests.Support;
using Khadra.WebAPI;
using Khadra.WebAPI.Controllers;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

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
    [InlineData("GET", "/api/v1/financial-documents/{0}/pdf-link?language=en")]
    [InlineData("GET", "/api/v1/bookings/{0}/financial-documents")]
    [InlineData("GET", "/api/v1/admin/financial-documents")]
    [InlineData("GET", "/api/v1/admin/financial-documents/{0}")]
    [InlineData("GET", "/api/v1/admin/financial-documents/{0}/pdf-link?language=ar")]
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

    // ── Nothing of a customer's documents is left in a cache (owner, 2026-09-27, decision D6) ─────────

    private const string NoStore = "no-store, private";

    [Fact]
    public async Task The_customers_list_of_documents_is_kept_out_of_caches()
    {
        var mediator = Substitute.For<ISender>();
        mediator.Send(Arg.Any<ListMyFinancialDocumentsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<PagedResult<FinancialDocumentListItem>, Error>(PagedResult.Empty<FinancialDocumentListItem>(1, 20)));
        var controller = Over(new FinancialDocumentsController(Customer()), mediator);

        await controller.Mine(null, null, null, CancellationToken.None);

        Assert.Equal(NoStore, controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task A_document_is_kept_out_of_caches_and_so_is_the_answer_that_it_does_not_exist()
    {
        var mediator = Substitute.For<ISender>();
        mediator.Send(Arg.Any<GetMyFinancialDocumentQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<FinancialDocumentDto, Error>(FinancialDocumentErrors.NotFound));
        var controller = Over(new FinancialDocumentsController(Customer()), mediator);

        var answer = await controller.Document(SomeId, CancellationToken.None);

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsType<ObjectResult>(answer).StatusCode);
        Assert.Equal(NoStore, controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task A_link_to_a_documents_pdf_is_kept_out_of_caches_and_so_is_the_answer_that_it_is_not_ready()
    {
        var mediator = Substitute.For<ISender>();
        mediator.Send(Arg.Any<GetMyFinancialDocumentPdfLinkQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<SignedDocumentLink, Error>(FinancialDocumentErrors.PdfNotReady));
        var controller = Over(new FinancialDocumentsController(Customer()), mediator);

        var answer = await controller.PdfLink(SomeId, "ar", CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsType<ObjectResult>(answer).StatusCode);
        Assert.Equal(NoStore, controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public void A_pdf_is_downloaded_where_every_private_file_is_and_under_the_private_documents_limit()
    {
        // No dedicated route for PDFs (payments Phase 6): the signed link points at the one download endpoint,
        // which still needs a session and now shares the private-documents bucket, not the ten-a-minute auth one.
        var download = typeof(DocumentsController).GetMethod(nameof(DocumentsController.Download))!;

        Assert.Equal(RateLimitPolicies.PrivateDocuments, download.GetCustomAttribute<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()?.PolicyName);
        Assert.Null(download.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(typeof(DocumentsController).GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Equal(
            "financial-documents/{documentId:guid}/pdf-link",
            typeof(FinancialDocumentsController).GetMethod(nameof(FinancialDocumentsController.PdfLink))!.GetCustomAttribute<HttpGetAttribute>()!.Template);
    }

    [Fact]
    public async Task A_bookings_documents_are_kept_out_of_caches()
    {
        var mediator = Substitute.For<ISender>();
        mediator.Send(Arg.Any<GetBookingFinancialDocumentsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<BookingFinancialDocumentsDto, Error>(new BookingFinancialDocumentsDto(SomeId, [], [])));
        var controller = Over(new BookingsController(Customer()), mediator);

        await controller.GetFinancialDocuments(SomeId, CancellationToken.None);

        Assert.Equal(NoStore, controller.Response.Headers.CacheControl.ToString());
    }

    // ── …nor of the administrator's documents, nor of a booking's financials (owner, 2026-09-28) ──────────

    [Fact]
    public async Task Every_answer_about_the_administrators_documents_is_kept_out_of_caches_refusals_included()
    {
        var mediator = Substitute.For<ISender>();
        mediator.Send(Arg.Any<ListAdminFinancialDocumentsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<PagedResult<AdminFinancialDocumentListItem>, Error>(PagedResult.Empty<AdminFinancialDocumentListItem>(1, 20)));
        mediator.Send(Arg.Any<GetFinancialDocumentVocabularyQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<FinancialDocumentVocabularyDto, Error>(FinancialDocumentErrors.NotFound));
        mediator.Send(Arg.Any<ListFinancialDocumentHoldsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<PagedResult<FinancialDocumentHoldDto>, Error>(PagedResult.Empty<FinancialDocumentHoldDto>(1, 20)));
        mediator.Send(Arg.Any<GetAdminFinancialDocumentQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<AdminFinancialDocumentDto, Error>(FinancialDocumentErrors.NotFound));
        mediator.Send(Arg.Any<GetAdminBookingFinancialDocumentsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<AdminBookingFinancialDocumentsDto, Error>(FinancialDocumentErrors.NotFound));
        mediator.Send(Arg.Any<VoidFinancialDocumentCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<VoidedFinancialDocumentDto, Error>(FinancialDocumentErrors.AlreadyVoided));
        mediator.Send(Arg.Any<GetAdminFinancialDocumentPdfLinkQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<SignedDocumentLink, Error>(FinancialDocumentErrors.PdfNotReady));

        List<Func<AdminFinancialDocumentsController, Task<ActionResult>>> answers =
        [
            controller => controller.List(null, null, null, null, null, null, null, null, CancellationToken.None),
            controller => controller.Vocabulary(CancellationToken.None),
            controller => controller.Holds(null, null, CancellationToken.None),
            controller => controller.Document(SomeId, CancellationToken.None),
            controller => controller.ForBooking(SomeId, CancellationToken.None),
            controller => controller.Void(SomeId, new AdminFinancialDocumentsController.VoidRequest("Wrong."), CancellationToken.None),
            controller => controller.PdfLink(SomeId, "en", CancellationToken.None),
        ];
        // Every action is here: one added later without the header fails this count first.
        Assert.Equal(
            typeof(AdminFinancialDocumentsController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Length,
            answers.Count);

        foreach (var answer in answers)
        {
            var controller = Over(new AdminFinancialDocumentsController(Customer()), mediator);
            await answer(controller);
            Assert.Equal(NoStore, controller.Response.Headers.CacheControl.ToString());
        }
    }

    [Fact]
    public async Task A_bookings_financials_are_kept_out_of_caches_for_its_parties_and_for_the_administrator()
    {
        var mediator = Substitute.For<ISender>();
        mediator.Send(Arg.Any<GetBookingFinancialsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<BookingFinancialsDto, Error>(BookingErrors.NotFound));
        mediator.Send(Arg.Any<GetAnyBookingFinancialsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<BookingFinancialsDto, Error>(BookingErrors.NotFound));

        var parties = Over(new BookingsController(Customer()), mediator);
        await parties.GetFinancials(SomeId, CancellationToken.None);
        Assert.Equal(NoStore, parties.Response.Headers.CacheControl.ToString());

        var administrator = Over(new AdminBookingsController(), mediator);
        await administrator.GetFinancials(SomeId, CancellationToken.None);
        Assert.Equal(NoStore, administrator.Response.Headers.CacheControl.ToString());
    }

    private static ICurrentActor Customer()
    {
        var actor = Substitute.For<ICurrentActor>();
        actor.UserId.Returns(Id.New());
        return actor;
    }

    /// <summary>The real controller over a substituted mediator, with a response to write headers on.</summary>
    private static TController Over<TController>(TController controller, ISender mediator)
        where TController : ControllerBase
    {
        var services = new ServiceCollection();
        services.AddSingleton(mediator);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() },
        };
        return controller;
    }
}
