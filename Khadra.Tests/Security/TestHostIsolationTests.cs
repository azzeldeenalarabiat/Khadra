using Khadra.Infrastructure.Configuration;
using Khadra.Infrastructure.Persistence;
using Khadra.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Tests.Security;

/// <summary>
/// A test host must not be able to reach the developer's own database, or migrate anything.
/// </summary>
/// <remarks>
/// <para>
/// On 2026-09-17 a plain <c>dotnet test</c> applied a pending migration to the local <c>khadra_e2e</c>.
/// Every factory in this suite already set a connection string that goes nowhere and
/// <c>Database:AutoMigrate=false</c> — with <c>UseSetting</c>, which <c>Program.cs</c> then overrode by
/// re-adding <c>appsettings.Development.json</c> and the developer's user-secrets on top. In a
/// Development-environment test the real connection string came back, auto-migrate read true, and the
/// host migrated the database the developer was working on.
/// </para>
/// <para>
/// Nothing about that was visible from the test: it passed. So the settings are asserted here, in the
/// environments a host is started in, rather than trusted to stay where they were put.
/// </para>
/// </remarks>
public sealed class TestHostIsolationTests
{
    private static WebApplicationFactory<Khadra.WebAPI.WebApiAssemblyMarker> Api(string environment) =>
        new WebApplicationFactory<Khadra.WebAPI.WebApiAssemblyMarker>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environment);
                builder.IsolateFromDeveloperDatabase();
                builder.UseSetting("Authentication:Jwt:SigningKey", new string('k', 48));
                builder.UseSetting("KnownProxies:0", "10.255.255.1");
                builder.UseSetting("Email:Provider", "Logging");
            });

    // Development is the one that bit, because it is the only environment that loads user-secrets —
    // but appsettings.Local.json is re-added in every environment, so every environment is asserted.
    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("Staging")]
    public void A_test_host_takes_a_database_it_cannot_reach_and_migrates_nothing(string environment)
    {
        using var factory = Api(environment);
        using var scope = factory.Services.CreateScope();

        var configuration = factory.Services.GetRequiredService<IConfiguration>();
        var database = factory.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        // What the DbContext was actually BUILT with, which is the thing that bit: the configuration
        // read afterwards was already the harmless one while the context held the developer's.
        var connection = scope.ServiceProvider
            .GetRequiredService<KhadraDbContext>()
            .Database.GetConnectionString();

        Assert.Equal(
            TestHostConfiguration.UnreachableConnection,
            configuration.GetConnectionString("DefaultConnection"));
        Assert.Contains("khadra_tests", connection, StringComparison.Ordinal);
        Assert.DoesNotContain("khadra_e2e", connection, StringComparison.Ordinal);
        Assert.False(
            database.AutoMigrate,
            $"a {environment} test host would migrate whatever database it just resolved");
    }
}
