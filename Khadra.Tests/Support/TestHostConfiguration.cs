using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Khadra.Tests.Support;

/// <summary>
/// The two settings a test host must never lose: a database it cannot reach, and no migrations.
/// </summary>
/// <remarks>
/// <para>
/// <c>UseSetting</c> is not enough, and the gap cost a real database its schema. <c>Program.cs</c>
/// re-adds <c>appsettings.{Environment}.json</c>, the gitignored <c>appsettings.Local.json</c> and —
/// in Development — the developer's user-secrets AFTER the host's own settings. So in a test host
/// those outrank anything <c>UseSetting</c> put there: the developer's real connection string comes
/// back, <c>Database:AutoMigrate</c> reads true from <c>appsettings.Development.json</c>, and starting
/// the host MIGRATES THE DEVELOPER'S OWN DATABASE. On 2026-09-17 a plain <c>dotnet test</c> applied a
/// pending migration to the local <c>khadra_e2e</c> that way, before anyone had approved it.
/// </para>
/// <para>
/// An appended in-memory source is the highest precedence there is — which is why the mail transport
/// test already states <c>Email:Provider</c> this way — so every factory in this suite states the
/// database the same way, whatever environment it runs in.
/// </para>
/// </remarks>
internal static class TestHostConfiguration
{
    /// <summary>A database nobody has, on credentials nobody holds, at an address nothing listens on.</summary>
    /// <remarks>
    /// Port 1 rather than Postgres's own (2026-10-01, after pre-launch item 221), so every host meets the same outage
    /// whatever runs on this machine: on 5432 a developer's running Postgres answered with a refused login and an idle
    /// machine with a refused connection, and the suite once passed or failed on which. And one second, because Windows
    /// answers a refused local connection only after retrying it, and every test host meets it at boot.
    /// </remarks>
    public const string UnreachableConnection =
        "Host=127.0.0.1;Port=1;Database=khadra_tests;Username=x;Password=y;Timeout=1";

    /// <summary>
    /// Said in the one voice <c>Program.cs</c> cannot talk over, for the whole test process, before a
    /// single test runs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two obvious ways do not work, and looking at the test could not tell you so. <c>UseSetting</c>
    /// lands BEFORE <c>Program.cs</c> re-adds <c>appsettings.Local.json</c> and the developer's
    /// user-secrets, which then outrank it — that is the bug. A test's
    /// <c>ConfigureAppConfiguration</c> lands AFTER the services were registered, so it fixes what the
    /// final configuration reads and not what the DbContext was actually built with.
    /// </para>
    /// <para>
    /// Environment variables are the last source <c>Program.cs</c> adds to its own chain, in every
    /// environment, before anything reads a connection string. A module initializer sets them before
    /// the first test — so a factory that forgets everything else is still harmless.
    /// </para>
    /// </remarks>
    [ModuleInitializer]
    internal static void IsolateTheTestProcessFromTheDevelopersDatabase()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", UnreachableConnection);
        Environment.SetEnvironmentVariable("Database__AutoMigrate", "false");

        // And no administrator bootstrap. `AdminBootstrapper` returns before touching the database
        // only while this is blank — which is true of the tracked settings and NOT true of a
        // developer's user-secrets, so a Development-environment test host would otherwise start by
        // asking the developer's own database whether it has an administrator.
        Environment.SetEnvironmentVariable("Admin__Bootstrap__Email", string.Empty);

        // And no test issuer identity (payments Phase 5). A developer may keep one in user-secrets to
        // issue local sandbox documents (owner, 2026-09-27); a Development-environment test host would
        // load it, and Program.cs refuses to start any host that is not on the sandbox with one. "false"
        // rather than empty: an empty value deletes the variable on Windows instead of overriding.
        Environment.SetEnvironmentVariable("FinancialDocuments__Issuer__TestIdentity", "false");
    }

    /// <summary>The same two settings again, for whatever a host reads after it has started.</summary>
    public static IWebHostBuilder IsolateFromDeveloperDatabase(this IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.ConfigureAppConfiguration(configuration =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = UnreachableConnection,
                ["Database:AutoMigrate"] = "false",
            }));
    }
}
