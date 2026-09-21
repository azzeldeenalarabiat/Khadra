using System.Net;
using CSharpFunctionalExtensions;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Khadra.Domain.Payments;
using Khadra.Infrastructure;
using Khadra.Infrastructure.Configuration;
using Khadra.Infrastructure.Payments;
using Khadra.Infrastructure.Persistence;
using Khadra.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Khadra.Tests.Security;

/// <summary>
/// The three things that keep a provider which confirms bookings without money out of Production.
/// </summary>
/// <remarks>
/// <para>
/// The standing rule is that no provider simulating a successful capture may ever be registered: a
/// simulated capture writes the same <c>Confirmed</c> status, the same row and the same dealer screen
/// as a real one, and within a day nobody can tell which rentals have money behind them. The owner
/// approved <c>SANDBOX</c> as a recorded exception on 2026-09-21, on the condition that operating it
/// in Production be made structurally impossible. Three mechanisms do that, and this file is the
/// proof that each of them actually fires:
/// </para>
/// <list type="number">
/// <item><b>Configuration.</b> Only two provider names exist, and an unrecognised one is refused
/// rather than quietly resolving to the refusing provider — because a typo that "works" looks exactly
/// like a deliberate <c>None</c>.</item>
/// <item><b>Environment.</b> A Production host refuses to start on the sandbox at all.</item>
/// <item><b>Data.</b> Any host refuses to start on the sandbox against a database that has ever held
/// a payment from another provider — the guard that catches a staging process pointed at the
/// production connection string, which is the likeliest way this actually goes wrong.</item>
/// </list>
/// <para>
/// None of the three is sufficient alone. An environment variable can be forgotten, copied or
/// overridden; a fresh production database on launch day is legitimately empty, so the data guard
/// would wave it through; and neither of them notices a misspelled setting.
/// </para>
/// </remarks>
public sealed class SandboxPaymentGuardTests
{
    private const string Secret = "a-sandbox-webhook-secret-for-the-tests";

    /// <summary>Where the sandbox checkout page would be served, as a phone would reach it.</summary>
    private const string ConsoleBaseUrl = "http://192.0.2.10:5012";

    // ------------------------------------------------------------------ 1. which provider is chosen

    /// <summary>
    /// A container built from configuration alone, the way the real host builds one.
    /// </summary>
    /// <remarks>
    /// A null <paramref name="provider"/> OMITS the key rather than setting it to null, because that
    /// is what an unset environment variable actually does: the binder never touches the property and
    /// the declared default survives. Writing a null in would be a different state — an explicitly
    /// blank setting — which is <see cref="A_blank_provider_refuses_to_start"/> below, and a
    /// different answer.
    /// </remarks>
    private static ServiceProvider Container(
        string? provider,
        string? webhookSecret = Secret,
        string? consoleBaseUrl = ConsoleBaseUrl)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=khadra_tests;Username=x;Password=y",
            ["Authentication:Jwt:SigningKey"] = new string('k', 48),
            ["Documents:AllowedContentTypes:0"] = "image/jpeg",
            ["Payments:WebhookSecret"] = webhookSecret,
            ["Payments:SandboxConsoleBaseUrl"] = consoleBaseUrl,
        };
        if (provider is not null) settings["Payments:Provider"] = provider;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(
            new Microsoft.Extensions.Hosting.Internal.HostingEnvironment
            {
                ContentRootPath = Path.GetTempPath(),
                EnvironmentName = "Testing",
                ApplicationName = "Khadra.Tests",
            });
        services.AddInfrastructureServices(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Refusal is the default. A platform with nothing configured takes no money, and says so.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("None")]
    [InlineData("none")]
    [InlineData("  None  ")]
    public void Nothing_configured_selects_the_provider_that_refuses(string? provider) =>
        Assert.IsType<UnconfiguredPaymentProvider>(
            Container(provider).GetRequiredService<IPaymentProvider>());

    [Theory]
    [InlineData("SANDBOX")]
    [InlineData("sandbox")]
    [InlineData("Sandbox")]
    [InlineData("  SANDBOX  ")]
    public void The_sandbox_is_selected_whatever_the_case_or_spacing(string provider) =>
        Assert.IsType<SandboxPaymentProvider>(
            Container(provider).GetRequiredService<IPaymentProvider>());

    /// <summary>
    /// A name nothing implements is refused, rather than falling back to the one that refuses.
    /// </summary>
    /// <remarks>
    /// The lesson the mail transport and the document store both taught, in the form it takes here. A
    /// silent fallback would "work" — every checkout refused, the boot log reading PAYMENTS ARE NOT
    /// ACCEPTED — so a misspelled <c>SANDBOX</c> would be indistinguishable from a deliberate
    /// <c>None</c>, and somebody would spend an afternoon wondering why the sandbox did nothing.
    /// </remarks>
    [Theory]
    [InlineData("SANDBOXX")]
    [InlineData("SANBOX")]
    [InlineData("\"SANDBOX\"")]
    [InlineData("Stripe")]
    [InlineData("Simulated")]
    public void An_unrecognised_provider_refuses_to_start(string provider)
    {
        var failure = Assert.Throws<OptionsValidationException>(
            () => Container(provider).GetRequiredService<IPaymentProvider>());

        // The message names the closed set, so the fix is on screen rather than in a file somewhere.
        Assert.Contains(PaymentOptions.NoProvider, failure.Message, StringComparison.Ordinal);
        Assert.Contains(PaymentOptions.SandboxProvider, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A setting present but blank is refused, and refused in words about payments.
    /// </summary>
    /// <remarks>
    /// Not the same state as an unset variable, and it reaches the code differently: an absent key
    /// leaves the declared default alone, while <c>Payments__Provider=</c> binds null OVER it. That
    /// null used to reach <c>.Trim()</c> inside options validation and surface as a bare
    /// <c>NullReferenceException</c> naming neither the section nor the setting — which is how a
    /// three-word configuration mistake becomes an hour with a stack trace.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_provider_refuses_to_start(string provider)
    {
        var failure = Assert.Throws<OptionsValidationException>(
            () => Container(provider).GetRequiredService<IPaymentProvider>());

        Assert.Contains("Provider", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The sandbox without a webhook secret is refused at boot rather than at the first checkout.
    /// </summary>
    /// <remarks>
    /// It signs its own deliveries, and the webhook endpoint is necessarily anonymous — a payment
    /// provider has no session with this platform. On any reachable host an unsigned sandbox would let
    /// a customer confirm their own booking by posting a capture. Refusing here beats a checkout that
    /// opens and then cannot be completed.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void The_sandbox_without_a_webhook_secret_refuses_to_start(string? secret)
    {
        var failure = Assert.Throws<OptionsValidationException>(
            () => Container(PaymentOptions.SandboxProvider, secret).GetRequiredService<IPaymentProvider>());

        Assert.Contains("WebhookSecret", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>And the absent secret does not bother the provider that takes no money anyway.</summary>
    [Fact]
    public void No_provider_needs_no_webhook_secret() =>
        Assert.IsType<UnconfiguredPaymentProvider>(
            Container(PaymentOptions.NoProvider, webhookSecret: null).GetRequiredService<IPaymentProvider>());

    /// <summary>
    /// The sandbox needs an absolute address for its own checkout page, or it is refused at boot.
    /// </summary>
    /// <remarks>
    /// A phone opens that link in its own browser, so a relative path or the console's address gets a
    /// customer a Pay button that goes nowhere — and the row would already have been written and the
    /// checkout "opened" by then. An ASP.NET process cannot work its own public address out: behind
    /// a proxy the host header is the proxy's, and on a LAN the binding is 0.0.0.0. So it is stated.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/sandbox-checkout")]
    [InlineData("192.0.2.10:5012")]
    [InlineData("ftp://192.0.2.10")]
    public void The_sandbox_without_an_absolute_console_address_refuses_to_start(string? consoleBaseUrl)
    {
        var failure = Assert.Throws<OptionsValidationException>(
            () => Container(PaymentOptions.SandboxProvider, consoleBaseUrl: consoleBaseUrl)
                .GetRequiredService<IPaymentProvider>());

        Assert.Contains("SandboxConsoleBaseUrl", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>And nothing else is asked for one.</summary>
    [Fact]
    public void No_provider_needs_no_console_address() =>
        Assert.IsType<UnconfiguredPaymentProvider>(
            Container(PaymentOptions.NoProvider, consoleBaseUrl: null).GetRequiredService<IPaymentProvider>());

    // ------------------------------------------------------------------ 2. the environment guard

    private static WebApplicationFactory<Khadra.WebAPI.WebApiAssemblyMarker> Api(
        string environment,
        string? provider) =>
        new WebApplicationFactory<Khadra.WebAPI.WebApiAssemblyMarker>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environment);
                builder.IsolateFromDeveloperDatabase();
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=khadra_tests;Username=x;Password=y");
                builder.UseSetting("Authentication:Jwt:SigningKey", new string('k', 48));
                builder.UseSetting("Database:AutoMigrate", "false");
                builder.UseSetting("KnownProxies:0", "10.255.255.1");

                // NOT UseSetting, for the reason MailTransportConfigurationTests records: Program.cs
                // re-adds appsettings.Local.json after the host's own settings, so an untracked
                // developer file would outrank UseSetting and the test would pass or fail on whatever
                // that machine happens to hold. An appended source is the highest precedence there is.
                builder.ConfigureAppConfiguration(configuration =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Payments:Provider"] = provider,
                        ["Payments:WebhookSecret"] = Secret,
                        ["Payments:SandboxConsoleBaseUrl"] = ConsoleBaseUrl,
                        // Every OTHER Production guard satisfied, so that what throws below is this
                        // one. Without these the mail guard fires first and the test would pass while
                        // proving nothing about payments.
                        ["Email:Provider"] = "Resend",
                        ["Email:ApiKey"] = "re_not_a_real_key",
                        ["Email:FromAddress"] = "onboarding@resend.dev",
                        ["Documents:Provider"] = "Supabase",
                        ["Documents:Supabase:Url"] = "https://project.supabase.co",
                        ["Documents:Supabase:Bucket"] = "khadra-documents",
                        ["Documents:Supabase:ServiceKey"] = "not-a-real-key",
                    }));
            });

    /// <summary>Production will not start on it. At all, before it serves one request.</summary>
    [Theory]
    [InlineData("SANDBOX")]
    [InlineData("sandbox")]
    [InlineData("  SANDBOX  ")]
    public async Task Production_refuses_to_start_on_the_provider_that_takes_no_money(string provider)
    {
        using var factory = Api("Production", provider);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            using var client = factory.CreateClient();
            await client.GetAsync(new Uri("/health/live", UriKind.Relative));
        });

        Assert.Contains("Payments:Provider", failure.Message, StringComparison.Ordinal);
        Assert.Contains(PaymentOptions.SandboxProvider, failure.Message, StringComparison.Ordinal);
        // And it names the way out, because the safe value is the one somebody has to type next.
        Assert.Contains(PaymentOptions.NoProvider, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// And Production on the safe default starts, which is what makes the test above mean something.
    /// </summary>
    /// <remarks>
    /// Render leaves <c>ASPNETCORE_ENVIRONMENT</c> unset, which defaults to Production, so this is the
    /// live deployment's own path. If it could not boot, the guard above would be passing for the
    /// wrong reason.
    /// </remarks>
    [Fact]
    public async Task Production_still_starts_on_the_safe_default()
    {
        using var factory = Api("Production", PaymentOptions.NoProvider);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Every other environment gets PAST the environment guard — and is then stopped by the data one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>IsProduction</c> rather than <c>!IsDevelopment</c>, matching the mail and document guards
    /// beside it: a test or staging host legitimately has no card processor, and borrowing the
    /// stricter shape would make the lifecycle untestable anywhere but a developer's own machine. So
    /// what this test has to prove is that the ENVIRONMENT is not what stops these three.
    /// </para>
    /// <para>
    /// It cannot prove that by booting all the way, and the reason is the suite's own design rather
    /// than a limitation: <c>TestHostConfiguration</c> points every test host at a database nobody
    /// has, deliberately, after a <c>dotnet test</c> once migrated a developer's real one. The
    /// sandbox refuses to run against a database it cannot read, so these hosts stop there — one step
    /// LATER than Production does, on a different question, which is exactly the distinction under
    /// test. The assertions name both: the message must be the data guard's, and must not be the
    /// environment guard's.
    /// </para>
    /// <para>
    /// That the pipeline boots whole on the sandbox is covered where a real database exists — the
    /// manual lifecycle run against local Postgres — rather than pretended at here.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("Staging")]
    public async Task Every_other_environment_gets_past_the_environment_guard(string environment)
    {
        using var factory = Api(environment, PaymentOptions.SandboxProvider);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            using var client = factory.CreateClient();
            await client.GetAsync(new Uri("/health/live", UriKind.Relative));
        });

        Assert.Contains("could not read the payments table", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("must never run in Production", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// And none of them is stopped when the sandbox is not asked for: the data guard is its alone.
    /// </summary>
    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("Staging")]
    public async Task Every_other_environment_starts_normally_without_the_sandbox(string environment)
    {
        using var factory = Api(environment, PaymentOptions.NoProvider);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ------------------------------------------------------------------ 3. the database guard

    /// <summary>
    /// Boots the startup check over a real relational database holding whatever rows are named.
    /// </summary>
    /// <remarks>
    /// SQLite rather than a substitute, because the guard is a QUERY and a fake repository would
    /// prove only that the fake was asked. What is under test is that the question reaches a database
    /// and that its answer is acted on.
    /// </remarks>
    private static async Task<IServiceProvider> DatabaseHolding(
        IPaymentProvider provider,
        params string[] providersAlreadyPaidThrough)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<KhadraDbContext>()
            .UseSqlite(connection)
            .UseSnakeCaseNamingConvention()
            .Options;

        await using (var seed = new KhadraDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            foreach (var name in providersAlreadyPaidThrough)
            {
                seed.Payments.Add(Payment.Open(
                    Id.New(),
                    Id.New(),
                    Money.Jod(75m),
                    name,
                    Build.Now.AddMinutes(30),
                    Build.Now));
            }

            await seed.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(provider);
        services.AddSingleton<IPaymentProviderProbe>(new FixedProbe(provider.Name));
        services.AddScoped(_ => new KhadraDbContext(options));
        return services.BuildServiceProvider();
    }

    private sealed class FixedProbe(string detail) : IPaymentProviderProbe
    {
        public Task<string> DescribeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(detail);
    }

    private static SandboxPaymentProvider Sandbox() =>
        new(Options.Create(new PaymentOptions
        {
            Provider = PaymentOptions.SandboxProvider,
            WebhookSecret = Secret,
        }),
        new TestClock(Build.Now));

    /// <summary>
    /// A database that has never taken a payment is the ordinary case, and it passes.
    /// </summary>
    /// <remarks>
    /// It has to. A developer machine's database is empty, and so is a real one the day before
    /// launch — which is exactly why the environment guard exists as well, and why neither guard is
    /// enough on its own.
    /// </remarks>
    [Fact]
    public async Task The_sandbox_starts_against_a_database_that_has_taken_nothing() =>
        await Khadra.WebAPI.PaymentsStartupCheck.ReportAsync(await DatabaseHolding(Sandbox()));

    /// <summary>And a database holding only its own sandbox rows is still its own to use.</summary>
    [Fact]
    public async Task The_sandbox_starts_against_a_database_holding_only_sandbox_payments() =>
        await Khadra.WebAPI.PaymentsStartupCheck.ReportAsync(
            await DatabaseHolding(Sandbox(), PaymentOptions.SandboxProvider, PaymentOptions.SandboxProvider));

    /// <summary>
    /// The guard that matters: a database that has handled real money never runs a fake.
    /// </summary>
    /// <remarks>
    /// This is the one that catches a staging or local process pointed at the production connection
    /// string — the likeliest way the exception would actually be abused, and one the environment
    /// variable cannot see, because that process genuinely is not Production.
    /// </remarks>
    [Theory]
    [InlineData("HyperPay")]
    [InlineData("MEPS")]
    [InlineData("Stripe")]
    public async Task The_sandbox_refuses_a_database_that_has_taken_a_real_payment(string realProvider)
    {
        var services = await DatabaseHolding(Sandbox(), realProvider);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Khadra.WebAPI.PaymentsStartupCheck.ReportAsync(services));

        // It names the provider it found, so nobody has to query the database to understand the
        // refusal — and so a surprising name is visible rather than summarised away.
        Assert.Contains(realProvider, failure.Message, StringComparison.Ordinal);
        Assert.Contains(PaymentOptions.SandboxProvider, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>One real row among its own is enough. The guard asks "ever", not "mostly".</summary>
    [Fact]
    public async Task One_real_payment_among_the_sandbox_rows_is_enough_to_refuse()
    {
        var services = await DatabaseHolding(
            Sandbox(),
            PaymentOptions.SandboxProvider,
            PaymentOptions.SandboxProvider,
            "HyperPay",
            PaymentOptions.SandboxProvider);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Khadra.WebAPI.PaymentsStartupCheck.ReportAsync(services));

        Assert.Contains("HyperPay", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Real payments are no obstacle to the provider that takes none. That is not what is guarded.
    /// </summary>
    /// <remarks>
    /// The question is "are this database's payments the same kind of money as this process can
    /// move", not "is this database tidy". A platform with no provider opens no attempts, so a
    /// database of real payments is no reason to stop it serving the other ninety-nine per cent of
    /// the product — and this is the shape Production itself boots in.
    /// </remarks>
    [Fact]
    public async Task A_database_full_of_real_payments_does_not_stop_the_provider_that_refuses() =>
        await Khadra.WebAPI.PaymentsStartupCheck.ReportAsync(
            await DatabaseHolding(Unconfigured(), "HyperPay", "MEPS"));

    // ------------------------------------------------- 4. and the guard runs the OTHER way too

    /// <summary>
    /// A database that has taken sandbox payments is never served by anything else. Including None.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The direction that bites on launch day, and the one an environment variable cannot see at all.
    /// Sandbox-confirmed bookings read <c>Confirmed</c>, with the deposit marked paid and the gallery
    /// already told to prepare a car. Switch the real adapter on over the top of them and they are
    /// indistinguishable from rentals somebody paid for — on every screen, in every export, forever.
    /// That is the precise confusion the owner said must never be possible, so the rule is stated in
    /// both directions: sandbox rows and real rows never share a database.
    /// </para>
    /// <para>
    /// <c>None</c> is included on purpose, even though it can create no rows of its own. It is what
    /// Production runs, so it is the guard that would catch a production API pointed at a database
    /// somebody once played with — and it is also what publishes <c>mode: None</c> to every client,
    /// which would suppress the sandbox banner while the sandbox rows were still on screen.
    /// </para>
    /// <para>
    /// The cost is real and deliberate: a developer who has run the sandbox against a database must
    /// keep that database on the sandbox, or use another one. A database name is the cheap half of
    /// this trade.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_database_that_has_taken_sandbox_payments_refuses_the_provider_that_takes_none()
    {
        var services = await DatabaseHolding(Unconfigured(), PaymentProviders.Sandbox);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Khadra.WebAPI.PaymentsStartupCheck.ReportAsync(services));

        Assert.Contains(PaymentProviders.Sandbox, failure.Message, StringComparison.Ordinal);
        // And it names the way out rather than only the problem.
        Assert.Contains("Payments__Provider", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>And a real provider, the day it exists, is refused over the same rows.</summary>
    /// <remarks>
    /// Substituted rather than waiting for an adapter, because the rule has to be right BEFORE the
    /// adapter lands — by then the rows are already there, and a guard written afterwards is a guard
    /// written too late.
    /// </remarks>
    [Fact]
    public async Task A_database_that_has_taken_sandbox_payments_refuses_a_real_provider()
    {
        var services = await DatabaseHolding(new StubLiveProvider(), PaymentProviders.Sandbox);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Khadra.WebAPI.PaymentsStartupCheck.ReportAsync(services));

        Assert.Contains(PaymentProviders.Sandbox, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// And a real provider on a database of real payments is the ordinary case, untouched.
    /// </summary>
    [Fact]
    public async Task A_real_provider_is_fine_on_a_database_of_real_payments() =>
        await Khadra.WebAPI.PaymentsStartupCheck.ReportAsync(
            await DatabaseHolding(new StubLiveProvider(), "HyperPay", "HyperPay"));

    private static UnconfiguredPaymentProvider Unconfigured() =>
        new(Options.Create(new PaymentOptions()));

    /// <summary>
    /// A provider that reports <see cref="PaymentMode.Live"/>. It does nothing; only its mode is read.
    /// </summary>
    private sealed class StubLiveProvider : IPaymentProvider
    {
        public string Name => "HyperPay";

        public PaymentMode Mode => PaymentMode.Live;

        public Task<Result<CheckoutSession, Error>> CreateCheckoutAsync(
            CheckoutRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Result<ProviderEvent, Error> ParseEvent(
            string rawBody, IReadOnlyDictionary<string, string> headers) =>
            throw new NotSupportedException();

        public Task<Result<ProviderPaymentState, Error>> QueryAsync(
            string providerReference, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<ProviderRefund, Error>> RefundAsync(
            RefundRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
