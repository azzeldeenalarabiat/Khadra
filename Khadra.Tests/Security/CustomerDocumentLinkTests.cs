using System.Net;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.IdentityAccess.Documents;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using Khadra.Infrastructure.Configuration;
using Khadra.Infrastructure.Documents;
using Khadra.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Khadra.Tests.Security;

/// <summary>
/// Reading back a document you uploaded: who may mint a link, and what the link is worth on its own
/// (spec 7, pre-launch item 93).
/// </summary>
/// <remarks>
/// The customer app used to hand the signed URL to an external browser, which carries no bearer
/// token, so the download answered 401 and the customer saw a page of JSON. The fix was on the CLIENT
/// — it fetches the bytes through its own authenticated connection — and these tests pin the server
/// side that decision rests on: the endpoint really does require a session as well as a signature,
/// the link really is scoped to its owner, and a signature really does expire and really is bound to
/// one file.
///
/// Live-token cases are not reachable here: <c>OnTokenValidated</c> re-reads the account to check its
/// security stamp, so a real bearer needs a database this suite does not have. The authorization RULE
/// is therefore stated against the handler, which is where it lives, and the transport layer is
/// checked for the attribute-shaped mistakes nothing else would notice.
/// </remarks>
public sealed class CustomerDocumentLinkTests : IDisposable
{
    private readonly WebApplicationFactory<Khadra.WebAPI.WebApiAssemblyMarker> _factory =
        new WebApplicationFactory<Khadra.WebAPI.WebApiAssemblyMarker>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=khadra_tests;Username=x;Password=y");
                builder.UseSetting("Authentication:Jwt:SigningKey", new string('k', 48));
                builder.UseSetting("Database:AutoMigrate", "false");
                builder.UseSetting("Email:Provider", "Logging");
                // Without this the host refuses to start outside Development: an API that has not
                // been told where its proxy is cannot tell one client from another when rate limiting.
                builder.UseSetting("KnownProxies:0", "10.255.255.1");
            });

    public void Dispose() => _factory.Dispose();

    // ── The owner can read their own ─────────────────────────────────────────────

    [Fact]
    public async Task The_owner_gets_a_link_to_their_own_document()
    {
        var customer = Build.Customer();
        var document = customer.Documents.First();
        var (handler, signer) = HandlerFor(customer);

        var result = await handler.Handle(
            new CreateCustomerDocumentLinkQuery(customer.Id, document.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        // Not merely "a link": one this platform will accept back, for THIS file.
        var (key, expires, signature) = Parse(result.Value.Url);
        Assert.Equal(document.StorageKey, key);
        Assert.True(signer.IsValid(key, expires, signature, Build.Now));
    }

    // ── Ownership ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Another_customers_document_is_not_found_rather_than_forbidden()
    {
        // Two real customers, each with their own papers. The second asks for the first's.
        var owner = Build.Customer(email: "rana@example.jo", phone: "0791234567");
        var stranger = Build.Customer(email: "sami@example.jo", phone: "0797654321");
        var someoneElsesDocument = owner.Documents.First().Id;

        var (handler, _) = HandlerFor(stranger);

        var result = await handler.Handle(
            new CreateCustomerDocumentLinkQuery(stranger.Id, someoneElsesDocument),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        // NOT FORBIDDEN, deliberately: a 403 would confirm that the id names a real document, which
        // is the one thing a stranger holding a guess must not be able to learn.
        Assert.Equal(IdentityErrors.DocumentNotFound, result.Error);
    }

    [Fact]
    public async Task A_document_id_that_exists_nowhere_is_the_same_answer()
    {
        var customer = Build.Customer();
        var (handler, _) = HandlerFor(customer);

        var result = await handler.Handle(
            new CreateCustomerDocumentLinkQuery(customer.Id, Id.New()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IdentityErrors.DocumentNotFound, result.Error);
    }

    // ── The signature on its own ─────────────────────────────────────────────────

    [Fact]
    public void An_expired_signature_is_refused()
    {
        var signer = Signer();
        var link = signer.Sign("customers/abc/licence.pdf", Build.Now);
        var (key, expires, signature) = Parse(link.Url);

        Assert.True(signer.IsValid(key, expires, signature, Build.Now));
        // One second past the stamped expiry is past it.
        Assert.False(signer.IsValid(key, expires, signature, link.ExpiresAt.AddSeconds(1)));
    }

    [Fact]
    public void A_signature_is_bound_to_ONE_file()
    {
        var signer = Signer();
        var link = signer.Sign("customers/abc/licence.pdf", Build.Now);
        var (_, expires, signature) = Parse(link.Url);

        // The same signature, pointed at a different key: refused. Otherwise one valid link would be
        // a key to every document in the store.
        Assert.False(signer.IsValid("customers/abc/passport.pdf", expires, signature, Build.Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-signature")]
    public void A_missing_or_invented_signature_is_refused(string signature)
    {
        var signer = Signer();
        var link = signer.Sign("customers/abc/licence.pdf", Build.Now);
        var (key, expires, _) = Parse(link.Url);

        Assert.False(signer.IsValid(key, expires, signature, Build.Now));
    }

    [Fact]
    public void A_tampered_expiry_is_refused()
    {
        var signer = Signer();
        var link = signer.Sign("customers/abc/licence.pdf", Build.Now);
        var (key, expires, signature) = Parse(link.Url);

        // Pushing the expiry out by hand does not extend the link: the timestamp is signed too.
        Assert.False(signer.IsValid(key, expires + 86_400, signature, Build.Now));
    }

    // ── The transport ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Downloading_with_a_valid_signature_but_NO_session_is_401()
    {
        // THE REGRESSION. This is precisely what an external browser does, and precisely why the
        // customer app no longer hands it the URL. If this ever answers 200, the second protection
        // has been dropped and a leaked link works for anyone until it expires.
        var signer = Signer();
        var link = signer.Sign("customers/abc/licence.pdf", Build.Now);

        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(new Uri(link.Url, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Minting_a_link_without_a_session_is_401()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri(
            $"/api/v1/customers/me/documents/{Guid.CreateVersion7()}/link", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static HmacDocumentLinkSigner Signer()
    {
        var policy = Substitute.For<IDocumentPolicySettings>();
        policy.LinkLifetime.Returns(TimeSpan.FromMinutes(5));

        return new HmacDocumentLinkSigner(
            Options.Create(new JwtOptions { SigningKey = new string('k', 48) }),
            policy);
    }

    private static (CreateCustomerDocumentLinkHandler Handler, HmacDocumentLinkSigner Signer)
        HandlerFor(User customer)
    {
        var users = Substitute.For<IUserRepository>();
        users.GetByIdAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Build.Now);

        var signer = Signer();
        return (new CreateCustomerDocumentLinkHandler(users, signer, clock), signer);
    }

    /// <summary>Pulls the three signed parts back out of the URL the signer produced.</summary>
    private static (string Key, long Expires, string Signature) Parse(string url)
    {
        var uri = new Uri($"http://localhost{url}", UriKind.Absolute);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        var token = uri.AbsolutePath.Split('/').Last();

        var padded = token.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');

        return (
            System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded)),
            long.Parse(query["expires"]!, System.Globalization.CultureInfo.InvariantCulture),
            query["signature"]!);
    }
}
