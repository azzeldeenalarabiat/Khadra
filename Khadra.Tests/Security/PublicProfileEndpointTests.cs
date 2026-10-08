using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.IdentityAccess;
using Khadra.Infrastructure.Persistence;
using Khadra.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Khadra.Tests.Security;

/// <summary>
/// The customer page on the wire (pre-launch item 119): who may write it, and what an office that may not trade gives
/// away to a stranger — nothing. The application layer proves both one level down; these go through the policy
/// attributes, the reader and the serialiser, against a real model on SQLite inside the API host.
/// </summary>
public sealed class PublicProfileEndpointTests : IDisposable
{
    private const string OfficeWords = "No smoking in any car, ever.";
    private const string OfficeWordsAr = "ممنوع التدخين في أي سيارة.";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;
    private readonly WebApplicationFactory<Khadra.WebAPI.WebApiAssemblyMarker> _factory;

    private readonly User _employee = User.CreateEmployee(
        EmailAddress.Create("staff@gallery.jo").Value,
        PhoneNumber.Create("0790000002").Value,
        PersonName.Create("Sami Odeh").Value,
        PasswordHash.FromHash("hashed:temporary"),
        Build.Now);
    private readonly Dealer _suspended;
    private readonly Dealer _trading;

    public PublicProfileEndpointTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<KhadraDbContext>()
            .UseSqlite(_connection)
            .UseSnakeCaseNamingConvention()
            .Options;

        // An office that wrote its page in both languages, every section, none hidden — and was then suspended.
        _suspended = Build.ApprovedDealer(businessName: "Aqaba Coast Cars");
        var text = new LocalizedInput(OfficeWordsAr, OfficeWords);
        Assert.True(_suspended.UpdatePublicProfile(text, PublicProfile.Create(text, text, text, text, text, []).Value).IsSuccess);
        Assert.True(_suspended.Suspend(Id.New(), "Licence lapsed.", Build.Now).IsSuccess);
        // The control: the same page on an office that may trade, so a 404 above is the suspension and not a read
        // that fails on this host.
        _trading = Build.ApprovedDealer(businessName: "Petra Wheels", commercialRegistration: "654321");
        Assert.True(_trading.UpdatePublicProfile(text, PublicProfile.Create(text, text, text, text, text, []).Value).IsSuccess);

        using (var context = new KhadraDbContext(_options))
        {
            context.Database.EnsureCreated();
            context.Users.Add(_employee);
            context.Dealers.AddRange(_suspended, _trading);
            context.SaveChanges();
        }

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
                // The real model, on one in-memory SQLite connection, for every scope the host opens.
                builder.ConfigureTestServices(services => services.AddScoped(_ => new KhadraDbContext(_options)));
            });
    }

    public void Dispose()
    {
        _factory.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task A_member_of_staff_may_not_write_the_page()
    {
        using var client = ClientFor(_employee);

        using var response = await client.PutAsJsonAsync(
            new Uri("/api/v1/dealers/me/public-profile", UriKind.Relative),
            new { about = new { ar = "x", en = "x" }, hiddenSections = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("auth.forbidden", (await JsonOf(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_suspended_office_is_not_found_and_its_words_go_nowhere()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");

        using var response = await client.GetAsync(new Uri($"/api/v1/galleries/{_suspended.Id}", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(OfficeWords, body, StringComparison.Ordinal);
        Assert.DoesNotContain(OfficeWordsAr, body, StringComparison.Ordinal);
        Assert.DoesNotContain("Aqaba Coast Cars", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_trading_office_answers_with_its_own_words()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");

        using var response = await client.GetAsync(new Uri($"/api/v1/galleries/{_trading.Id}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(OfficeWords, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private HttpClient ClientFor(User user)
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<IAccessTokenIssuer>().Issue(user, Guid.NewGuid(), DateTimeOffset.UtcNow).Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
