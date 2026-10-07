using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Legal.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using Khadra.Domain.Legal;
using Khadra.Tests.Support;
using Khadra.WebAPI.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Khadra.Tests.Security;

/// <summary>
/// The legal-consent gate (Wave 4, W4-8; D6/D7, approved by the owner on 2026-10-07 with administrators exempt): a
/// website or console request from a person who has not accepted a text in force is refused, except what resolves the
/// gate, and nothing anonymous, nothing from the customer app and nothing an administrator sends is ever judged.
/// </summary>
public sealed class LegalConsentGateTests : IDisposable
{
    private readonly User _customer = Users.Customer();
    private readonly User _admin = User.CreateInvitedAdmin(
        EmailAddress.Create("staff@khadra.jo").Value,
        PhoneNumber.Create("0790000001").Value,
        PersonName.Create("Dana Saleh").Value,
        PasswordHash.FromHash("hashed:Passw0rd1"),
        Users.Now);
    private readonly ILegalConsentReader _consents = Substitute.For<ILegalConsentReader>();
    private readonly ILegalDocumentReader _texts = Substitute.For<ILegalDocumentReader>();
    private readonly WebApplicationFactory<Khadra.WebAPI.WebApiAssemblyMarker> _factory;
    private readonly PendingLegalVersion _terms = new(LegalDocumentKind.Terms, Id.New(), "2026-10", TestLegal.Published);

    public LegalConsentGateTests()
    {
        _admin.VerifyEmail(Users.Now);
        var users = Substitute.For<IUserRepository>();
        users.GetByIdAsync(_customer.Id, Arg.Any<CancellationToken>()).Returns(_customer);
        users.GetByIdAsync(_admin.Id, Arg.Any<CancellationToken>()).Returns(_admin);
        _consents.AcceptedAsync(Arg.Any<Id>(), Arg.Any<CancellationToken>()).Returns([]);
        _consents.AlreadyAcceptedAsync(Arg.Any<Id>(), Arg.Any<IReadOnlyCollection<Id>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<Id>());
        _texts.CurrentAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
        Pending(_terms);

        _factory = new WebApplicationFactory<Khadra.WebAPI.WebApiAssemblyMarker>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.IsolateFromDeveloperDatabase();
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=khadra_tests;Username=x;Password=y");
                builder.UseSetting("Authentication:Jwt:SigningKey", new string('k', 48));
                builder.UseSetting("Database:AutoMigrate", "false");
                builder.UseSetting("Email:Provider", "Logging");
                builder.UseSetting("KnownProxies:0", "10.255.255.1");
                // No database: the accounts as the stamp check reads them, the consents and texts as the gate and the
                // prompt read them, and a unit of work that saves nothing.
                builder.ConfigureTestServices(services =>
                {
                    services.AddScoped(_ => users);
                    services.AddScoped(_ => _consents);
                    services.AddScoped(_ => _texts);
                    services.AddScoped(_ => Substitute.For<IUnitOfWork>());
                });
            });
    }

    public void Dispose() => _factory.Dispose();

    private void Pending(params PendingLegalVersion[] pending) =>
        _consents.PendingAsync(Arg.Any<Id>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(pending);

    private HttpClient ClientFor(User user, string? appVersion = null)
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<IAccessTokenIssuer>().Issue(user, Guid.NewGuid(), DateTimeOffset.UtcNow).Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (appVersion is not null)
            client.DefaultRequestHeaders.Add("X-Khadra-App-Version", appVersion);
        return client;
    }

    private static async Task<bool> RefusedByTheGateAsync(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.Forbidden)
            return false;
        var body = await response.Content.ReadAsStringAsync();
        return body.Contains("legal.consent_pending", StringComparison.Ordinal);
    }

    // ── End to end, through the real pipeline ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_person_with_a_text_to_accept_is_refused_with_a_code_the_texts_and_no_store()
    {
        using var client = ClientFor(_customer);

        using var response = await client.GetAsync(new Uri("/api/v1/customers/me/documents", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("legal.consent_pending", problem.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(problem.RootElement.GetProperty("traceId").GetString()));
        var pending = Assert.Single(problem.RootElement.GetProperty("pending").EnumerateArray());
        Assert.Equal("Terms", pending.GetProperty("kind").GetString());
        Assert.Equal(_terms.VersionId.Value, pending.GetProperty("versionId").GetGuid());
        Assert.Equal("2026-10", pending.GetProperty("versionLabel").GetString());
    }

    /// <summary>Read on every request, never cached: an acceptance opens the gate at once, and a publish closes it.</summary>
    [Fact]
    public async Task Accepting_opens_the_gate_at_once_and_a_new_publish_closes_it_again()
    {
        using var client = ClientFor(_customer);
        var documents = new Uri("/api/v1/customers/me/documents", UriKind.Relative);

        using var before = await client.GetAsync(documents);
        Pending();
        using var accepted = await client.GetAsync(documents);
        Pending(_terms with { VersionId = Id.New(), VersionLabel = "2026-11" });
        using var republished = await client.GetAsync(documents);

        Assert.True(await RefusedByTheGateAsync(before));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.True(await RefusedByTheGateAsync(republished));
    }

    [Fact]
    public async Task What_resolves_the_gate_stays_open_while_it_is_closed()
    {
        using var client = ClientFor(_customer);

        using var me = await client.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative));
        using var record = await client.GetAsync(new Uri("/api/v1/auth/me/legal-consents", UriKind.Relative));
        using var accept = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/me/legal-consents", UriKind.Relative), new { versionIds = new[] { Guid.NewGuid() }, language = "ar" });
        using var language = await client.PutAsJsonAsync(new Uri("/api/v1/auth/me/language", UriKind.Relative), new { language = "ar" });
        using var signOut = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/logout", UriKind.Relative), new { refreshToken = "anything", allDevices = false });

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(HttpStatusCode.OK, record.StatusCode);
        // Through the gate, and answered by the handler: that version is not the one in force.
        Assert.Equal(HttpStatusCode.Conflict, accept.StatusCode);
        Assert.False(await RefusedByTheGateAsync(language));
        Assert.False(await RefusedByTheGateAsync(signOut));

        using var body = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        Assert.Equal("Terms", Assert.Single(body.RootElement.GetProperty("pendingConsents").EnumerateArray()).GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Anything_anonymous_is_never_judged_even_with_a_bearer()
    {
        using var client = ClientFor(_customer);

        using var config = await client.GetAsync(new Uri("/api/v1/app-config", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, config.StatusCode);
    }

    /// <summary>No installed build knows to ask, and an installed build cannot be patched, only refused.</summary>
    [Fact]
    public async Task The_customer_app_is_never_judged_by_the_version_it_declares()
    {
        using var client = ClientFor(_customer, appVersion: "1.3.0");

        using var documents = await client.GetAsync(new Uri("/api/v1/customers/me/documents", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, documents.StatusCode);
    }

    [Fact]
    public async Task An_administrator_is_never_judged()
    {
        using var client = ClientFor(_admin);

        using var counts = await client.GetAsync(new Uri("/api/v1/admin/legal-documents", UriKind.Relative));

        Assert.False(await RefusedByTheGateAsync(counts));
    }

    // ── Who is judged at all ─────────────────────────────────────────────────────────────────────────────

    private static DefaultHttpContext Request(bool anonymousEndpoint = false, bool allowed = false, string? version = null, string? userAgent = null, bool endpoint = true)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/bookings";
        if (version is not null)
            context.Request.Headers["X-Khadra-App-Version"] = version;
        if (userAgent is not null)
            context.Request.Headers.UserAgent = userAgent;
        if (endpoint)
        {
            var metadata = new List<object>();
            if (anonymousEndpoint)
                metadata.Add(new AllowAnonymousAttribute());
            if (allowed)
                metadata.Add(new AllowWhileConsentPendingAttribute());
            context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(metadata), "test"));
        }

        return context;
    }

    private static ICurrentActor Actor(UserRole? role, bool authenticated = true)
    {
        var actor = Substitute.For<ICurrentActor>();
        actor.IsAuthenticated.Returns(authenticated);
        actor.UserId.Returns(authenticated ? Id.New() : null);
        actor.Role.Returns(role);
        return actor;
    }

    [Fact]
    public void Every_signed_in_customer_and_office_request_is_judged()
    {
        Assert.True(LegalConsentGate.MustJudge(Request(), Actor(UserRole.Customer)));
        Assert.True(LegalConsentGate.MustJudge(Request(), Actor(UserRole.DealerOwner)));
        Assert.True(LegalConsentGate.MustJudge(Request(), Actor(UserRole.DealerEmployee)));
    }

    [Fact]
    public void Nobody_signed_in_an_anonymous_endpoint_and_the_gates_own_endpoints_are_never_judged()
    {
        Assert.False(LegalConsentGate.MustJudge(Request(), Actor(null, authenticated: false)));
        Assert.False(LegalConsentGate.MustJudge(Request(anonymousEndpoint: true), Actor(UserRole.Customer)));
        Assert.False(LegalConsentGate.MustJudge(Request(allowed: true), Actor(UserRole.Customer)));
        Assert.False(LegalConsentGate.MustJudge(Request(endpoint: false), Actor(UserRole.Customer)));
        Assert.False(LegalConsentGate.MustJudge(Request(), Actor(UserRole.Admin)));
    }

    /// <summary>
    /// The exemption is keyed on a DECLARED version (the advisor's review): a legacy User-Agent or an unreadable version
    /// is something anyone can type, and with no minimum configured neither would be refused first. When the minimum
    /// reaches the first build that asks for consent (1.4.0), the exemption is deleted: see pre-launch item 238.
    /// </summary>
    [Fact]
    public void Only_an_app_build_that_declared_its_version_is_spared()
    {
        Assert.False(LegalConsentGate.MustJudge(Request(version: "1.3.0"), Actor(UserRole.Customer)));
        Assert.True(LegalConsentGate.MustJudge(Request(version: "not-a-version"), Actor(UserRole.Customer)));
        Assert.True(LegalConsentGate.MustJudge(Request(userAgent: "Khadra (Android 16)"), Actor(UserRole.Customer)));
    }
}
