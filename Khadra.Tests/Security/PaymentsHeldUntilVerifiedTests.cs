using System.Data.Common;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using CSharpFunctionalExtensions;
using Khadra.Application.Common.Ports;
using Khadra.Application.IdentityAccess;
using Khadra.Application.IdentityAccess.AdminUsers;
using Khadra.Domain.Auditing.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using Khadra.Domain.Payments;
using Khadra.Infrastructure.Configuration;
using Khadra.Infrastructure.Payments;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Scheduling;
using Khadra.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Khadra.Tests.Security;

/// <summary>
/// A database that does not answer no longer stops the API, and a payment is still never taken on one the guard has
/// not read (pre-launch item 221; owner, 2026-10-01).
/// </summary>
/// <remarks>
/// <para>
/// Until this, a REFUSED connection reached the startup checks wrapped by EF's execution strategy in an
/// <see cref="InvalidOperationException"/>, which their <c>catch (DbException)</c> missed, and the host crashed at
/// boot for every provider. The owner's decision: the startup payments guard must not crash the API merely because
/// PostgreSQL is temporarily unreachable — and the guard's own promise, that sandbox and real money never share a
/// database, must not weaken.
/// </para>
/// <para>
/// So a configured provider starts HELD: it reads as unconfigured and refuses every provider call until the guard's
/// query reads cleanly, which opens it; finding the other kind of money stops the host. A database that answers at
/// boot is judged there, exactly as before (<see cref="SandboxPaymentGuardTests"/>, section 3).
/// </para>
/// </remarks>
public sealed class PaymentsHeldUntilVerifiedTests
{
    private const string Secret = "a-sandbox-webhook-secret-for-the-tests";
    private const string ConsoleBaseUrl = "http://192.0.2.10:5012";

    // ------------------------------------------------------------------ what counts as "could not read"

    [Fact]
    public async Task A_refused_connection_is_unreadable_however_EF_hands_it_over()
    {
        var options = new DbContextOptionsBuilder<KhadraDbContext>()
            .UseNpgsql(TestHostConfiguration.UnreachableConnection)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var context = new KhadraDbContext(options);

        var verdict = await PaymentDatabaseCheck.RunAsync(context, Sandbox());

        var unreadable = Assert.IsType<PaymentDatabaseVerdict.Unreadable>(verdict);
        // The exception the old `catch (DbException)` let through: not a DbException itself, one inside it.
        Assert.IsNotAssignableFrom<DbException>(unreadable.Cause);
        Assert.True(DatabaseUnreadable.IsCauseOf(unreadable.Cause));
        // And what is logged is the database's own sentence, not EF's advice to enable retries — the wrong fix.
        Assert.IsAssignableFrom<DbException>(DatabaseUnreadable.CauseOf(unreadable.Cause));
        Assert.DoesNotContain("EnableRetryOnFailure", DatabaseUnreadable.Reason(unreadable.Cause), StringComparison.Ordinal);
    }

    [Fact]
    public void Only_a_database_cause_counts()
    {
        Assert.True(DatabaseUnreadable.IsCauseOf(new InvalidOperationException("transient", new TimeoutException())));
        Assert.True(DatabaseUnreadable.IsCauseOf(new AggregateException(new SocketException())));
        Assert.True(DatabaseUnreadable.IsCauseOf(new SqliteException("no such table: payments", 1)));
        // The guard's own refusal is an InvalidOperationException, and must stay fatal.
        Assert.False(DatabaseUnreadable.IsCauseOf(new InvalidOperationException("Payments:Provider is 'SANDBOX', but …")));
        Assert.False(DatabaseUnreadable.IsCauseOf(new OperationCanceledException()));
        Assert.False(DatabaseUnreadable.IsCauseOf(null));

        // The cause is the innermost thing that came from the database or the network, wherever it sits.
        var timeout = new TimeoutException("Timeout during connecting");
        Assert.Same(timeout, DatabaseUnreadable.CauseOf(new InvalidOperationException("transient", timeout)));
        var socket = new SocketException();
        Assert.Same(socket, DatabaseUnreadable.CauseOf(new AggregateException(new InvalidOperationException("no"), socket)));
        Assert.Equal("Timeout during connecting", DatabaseUnreadable.Reason(new InvalidOperationException("transient", timeout)));
        Assert.Equal("not the database", DatabaseUnreadable.Reason(new InvalidOperationException("not the database")));
    }

    // ------------------------------------------------------------------ the hold itself

    [Fact]
    public async Task A_held_provider_takes_part_in_nothing_and_says_what_it_would_be()
    {
        var inner = Substitute.For<IPaymentProvider>();
        inner.Name.Returns(PaymentProviders.Sandbox);
        inner.Mode.Returns(PaymentMode.Sandbox);
        inner.IsConfigured.Returns(true);
        var held = new VerifiedPaymentProvider(inner, new PaymentVerification());

        // What it would be: /app-config, the consoles' Sandbox banner and every reminder read these.
        Assert.Equal(PaymentProviders.Sandbox, held.Name);
        Assert.Equal(PaymentMode.Sandbox, held.Mode);
        // What it does: nothing. Handlers decide on IsConfigured BEFORE they reach a provider call.
        Assert.False(held.IsConfigured);
        Assert.Equal(PaymentErrors.ProviderUnavailable, (await held.CreateCheckoutAsync(null!)).Error);
        Assert.Equal(PaymentErrors.ProviderUnavailable, held.ParseEvent("{}", new Dictionary<string, string>()).Error);
        Assert.Equal(PaymentErrors.ProviderUnavailable, (await held.QueryAsync("sbx_1")).Error);
        Assert.Equal(PaymentErrors.ProviderUnavailable, (await held.RefundAsync(null!)).Error);
        await inner.DidNotReceiveWithAnyArgs().CreateCheckoutAsync(default!, default);
        inner.DidNotReceiveWithAnyArgs().ParseEvent(default!, default!);
        await inner.DidNotReceiveWithAnyArgs().QueryAsync(default!, default);
        await inner.DidNotReceiveWithAnyArgs().RefundAsync(default!, default);
    }

    [Fact]
    public async Task Verified_it_is_the_provider_itself_and_refused_it_never_opens_again()
    {
        var inner = Substitute.For<IPaymentProvider>();
        inner.IsConfigured.Returns(true);
        var answer = Error.NotFound("inner.answered", "The provider itself was asked.");
        inner.QueryAsync("sbx_1", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ProviderPaymentState, Error>(answer));
        var verification = new PaymentVerification();
        var provider = new VerifiedPaymentProvider(inner, verification);

        verification.MarkVerified();
        Assert.True(provider.IsConfigured);
        Assert.Equal(answer, (await provider.QueryAsync("sbx_1")).Error);

        var refused = new PaymentVerification();
        refused.MarkRefused();
        refused.MarkVerified();
        Assert.False(new VerifiedPaymentProvider(inner, refused).IsConfigured);
    }

    // ------------------------------------------------------------------ at boot

    [Fact]
    public async Task At_boot_a_clean_read_opens_payments()
    {
        var services = await DatabaseHolding(Sandbox(), PaymentProviders.Sandbox);

        await Khadra.WebAPI.PaymentsStartupCheck.ReportAsync(services);

        Assert.True(services.GetRequiredService<PaymentVerification>().IsVerified);
        Assert.True(services.GetRequiredService<IPaymentProvider>().IsConfigured);
    }

    [Fact]
    public async Task At_boot_an_unreadable_table_starts_the_sandbox_held_instead_of_crashing()
    {
        var services = await DatabaseHolding(Sandbox(), dropPayments: true);

        await Khadra.WebAPI.PaymentsStartupCheck.ReportAsync(services);

        Assert.False(services.GetRequiredService<PaymentVerification>().IsSettled);
        Assert.False(services.GetRequiredService<IPaymentProvider>().IsConfigured);
    }

    /// <summary>
    /// With no provider an unreadable boot is not settled either, so the other direction is still asked: a database
    /// holding sandbox money refuses a process with none, and an unreadable boot used to skip that for good.
    /// </summary>
    [Fact]
    public async Task At_boot_with_no_provider_an_unreadable_table_is_asked_again_later()
    {
        var services = await DatabaseHolding(
            new UnconfiguredPaymentProvider(Options.Create(new PaymentOptions { Provider = PaymentOptions.NoProvider })),
            dropPayments: true);

        await Khadra.WebAPI.PaymentsStartupCheck.ReportAsync(services);

        Assert.False(services.GetRequiredService<PaymentVerification>().IsSettled);
    }

    [Fact]
    public async Task At_boot_the_other_kind_of_money_is_still_refused()
    {
        var services = await DatabaseHolding(Sandbox(), "HyperPay");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Khadra.WebAPI.PaymentsStartupCheck.ReportAsync(services));

        Assert.Contains("HyperPay", failure.Message, StringComparison.Ordinal);
        Assert.True(services.GetRequiredService<PaymentVerification>().IsRefused);
    }

    // ------------------------------------------------------------------ asked again until it can be

    [Fact]
    public async Task While_the_table_cannot_be_read_payments_stay_held_and_the_host_runs()
    {
        var services = await DatabaseHolding(Sandbox(), dropPayments: true);
        var lifetime = Substitute.For<IHostApplicationLifetime>();

        await Deferred(services, lifetime).RunOnceAsync(CancellationToken.None);
        await Deferred(services, lifetime).RunOnceAsync(CancellationToken.None);

        Assert.False(services.GetRequiredService<PaymentVerification>().IsSettled);
        lifetime.DidNotReceive().StopApplication();
    }

    [Fact]
    public async Task Once_it_can_be_read_cleanly_payments_open()
    {
        var services = await DatabaseHolding(Sandbox(), PaymentProviders.Sandbox);
        var lifetime = Substitute.For<IHostApplicationLifetime>();

        await Deferred(services, lifetime).RunOnceAsync(CancellationToken.None);

        Assert.True(services.GetRequiredService<PaymentVerification>().IsVerified);
        Assert.True(services.GetRequiredService<IPaymentProvider>().IsConfigured);
        lifetime.DidNotReceive().StopApplication();
    }

    /// <summary>The same refusal as at boot, in both directions, the moment the question can be answered.</summary>
    [Theory]
    [InlineData("sandbox", "HyperPay")]
    [InlineData("none", PaymentProviders.Sandbox)]
    [InlineData("live", PaymentProviders.Sandbox)]
    public async Task Once_it_can_be_read_the_other_kind_of_money_stops_the_host(string process, string held)
    {
        IPaymentProvider provider = process switch
        {
            "sandbox" => Sandbox(),
            "none" => new UnconfiguredPaymentProvider(Options.Create(new PaymentOptions { Provider = PaymentOptions.NoProvider })),
            _ => new LiveStub(),
        };
        var services = await DatabaseHolding(provider, held);
        var lifetime = Substitute.For<IHostApplicationLifetime>();

        try
        {
            await Deferred(services, lifetime).RunOnceAsync(CancellationToken.None);

            Assert.True(services.GetRequiredService<PaymentVerification>().IsRefused);
            Assert.False(services.GetRequiredService<IPaymentProvider>().IsConfigured);
            lifetime.Received(1).StopApplication();
            Assert.Equal(1, Environment.ExitCode);
        }
        finally
        {
            Environment.ExitCode = 0;
        }
    }

    [Fact]
    public async Task Once_settled_it_asks_nothing_more()
    {
        var services = await DatabaseHolding(Sandbox(), dropPayments: true);
        services.GetRequiredService<PaymentVerification>().MarkVerified();
        var lifetime = Substitute.For<IHostApplicationLifetime>();

        await Deferred(services, lifetime).RunOnceAsync(CancellationToken.None);

        Assert.True(services.GetRequiredService<PaymentVerification>().IsVerified);
        lifetime.DidNotReceive().StopApplication();
    }

    // ------------------------------------------------------------------ the first administrator, put off at boot

    /// <summary>While the database does not answer, the bootstrap waits, and the host runs.</summary>
    [Fact]
    public async Task A_bootstrap_put_off_waits_while_the_database_does_not_answer()
    {
        var (services, users) = BootstrapPutOff();
        users.AnyAdminExistsAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("transient", new SocketException()));
        var lifetime = Substitute.For<IHostApplicationLifetime>();

        await Deferred(services, lifetime).RunOnceAsync(CancellationToken.None);
        await Deferred(services, lifetime).RunOnceAsync(CancellationToken.None);

        Assert.True(services.GetRequiredService<DeferredStartupWork>().AdminBootstrapPending);
        lifetime.DidNotReceive().StopApplication();
    }

    /// <summary>Once it answers, the bootstrap runs — here, inviting the first administrator — and is done.</summary>
    [Fact]
    public async Task A_bootstrap_put_off_runs_once_the_database_answers()
    {
        var (services, users) = BootstrapPutOff();
        users.AnyAdminExistsAsync(Arg.Any<CancellationToken>()).Returns(false);
        users.ExistsByEmailAsync(Arg.Any<EmailAddress>(), Arg.Any<CancellationToken>()).Returns(false);
        users.ExistsByPhoneAsync(Arg.Any<PhoneNumber>(), Arg.Any<CancellationToken>()).Returns(false);
        var lifetime = Substitute.For<IHostApplicationLifetime>();

        await Deferred(services, lifetime).RunOnceAsync(CancellationToken.None);

        await users.Received(1).AddAsync(Arg.Is<User>(user => user.Email.Value == BootstrapSettings.Address), Arg.Any<CancellationToken>());
        Assert.False(services.GetRequiredService<DeferredStartupWork>().AdminBootstrapPending);
        lifetime.DidNotReceive().StopApplication();
    }

    /// <summary>
    /// And a refusal it finds then — the address already held by another account — stops the API, as it would have
    /// at boot.
    /// </summary>
    [Fact]
    public async Task A_bootstrap_put_off_and_then_refused_stops_the_host()
    {
        var (services, users) = BootstrapPutOff();
        users.AnyAdminExistsAsync(Arg.Any<CancellationToken>()).Returns(false);
        users.ExistsByEmailAsync(Arg.Any<EmailAddress>(), Arg.Any<CancellationToken>()).Returns(true);
        users.ExistsByPhoneAsync(Arg.Any<PhoneNumber>(), Arg.Any<CancellationToken>()).Returns(false);
        var lifetime = Substitute.For<IHostApplicationLifetime>();

        try
        {
            await Deferred(services, lifetime).RunOnceAsync(CancellationToken.None);

            lifetime.Received(1).StopApplication();
            Assert.Equal(1, Environment.ExitCode);
            Assert.False(services.GetRequiredService<DeferredStartupWork>().AdminBootstrapPending);
            await users.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        }
        finally
        {
            Environment.ExitCode = 0;
        }
    }

    // ------------------------------------------------------------------ the whole API, on a database that refuses
    //
    // Every host in this suite is pointed at a database nobody has (TestHostConfiguration), and that is the outage
    // these boot into.

    /// <summary>With no provider, the platform starts, as it always meant to.</summary>
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task With_no_provider_the_API_starts_on_a_database_that_refuses(string environment)
    {
        using var factory = Api(environment, PaymentOptions.NoProvider);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>With the sandbox, it starts too — and takes no payment, and says what it would be.</summary>
    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("Staging")]
    public async Task With_the_sandbox_the_API_starts_on_a_database_that_refuses_and_holds_every_payment(string environment)
    {
        using var factory = Api(environment, PaymentOptions.SandboxProvider);
        using var client = factory.CreateClient();

        using var live = await client.GetAsync(new Uri("/health/live", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);

        var provider = factory.Services.GetRequiredService<IPaymentProvider>();
        Assert.Equal(PaymentProviders.Sandbox, provider.Name);
        Assert.Equal(PaymentMode.Sandbox, provider.Mode);
        Assert.False(provider.IsConfigured);

        // A provider event is not taken in: 503, so a real provider delivers it again later.
        using var webhook = await client.PostAsync(
            new Uri("/api/v1/payments/webhooks/SANDBOX", UriKind.Relative),
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, webhook.StatusCode);
        using var problem = JsonDocument.Parse(await webhook.Content.ReadAsStringAsync());
        Assert.Equal("payments.provider_unavailable", problem.RootElement.GetProperty("code").GetString());
    }

    /// <summary>And a configured first-administrator bootstrap is put off, not dropped, and not fatal.</summary>
    [Fact]
    public async Task A_bootstrap_that_cannot_ask_the_database_is_put_off_until_it_can()
    {
        using var factory = Api("Staging", PaymentOptions.NoProvider, new Dictionary<string, string?>
        {
            ["Admin:Bootstrap:Email"] = "owner@example.com",
            ["Admin:Bootstrap:Phone"] = "0791234567",
            ["Admin:Bootstrap:FullName"] = "Platform Owner",
        });
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(factory.Services.GetRequiredService<DeferredStartupWork>().AdminBootstrapPending);
    }

    // ------------------------------------------------------------------ helpers

    private static WebApplicationFactory<Khadra.WebAPI.WebApiAssemblyMarker> Api(
        string environment,
        string provider,
        Dictionary<string, string?>? more = null) =>
        new WebApplicationFactory<Khadra.WebAPI.WebApiAssemblyMarker>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environment);
                builder.IsolateFromDeveloperDatabase();
                builder.UseSetting("Authentication:Jwt:SigningKey", new string('k', 48));
                builder.UseSetting("Database:AutoMigrate", "false");
                builder.UseSetting("KnownProxies:0", "10.255.255.1");
                var settings = new Dictionary<string, string?>
                {
                    ["Payments:Provider"] = provider,
                    ["Payments:WebhookSecret"] = Secret,
                    ["Payments:SandboxConsoleBaseUrl"] = ConsoleBaseUrl,
                    // Every Production guard but the one under test satisfied, as SandboxPaymentGuardTests does.
                    ["Email:Provider"] = "Resend",
                    ["Email:ApiKey"] = "re_not_a_real_key",
                    ["Email:FromAddress"] = "onboarding@resend.dev",
                    ["Documents:Provider"] = "Supabase",
                    ["Documents:Supabase:Url"] = "https://project.supabase.co",
                    ["Documents:Supabase:Bucket"] = "khadra-documents",
                    ["Documents:Supabase:ServiceKey"] = "not-a-real-key",
                };
                foreach (var (key, value) in more ?? []) settings[key] = value;
                builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(settings));
            });

    /// <summary>
    /// The guard's query over a real relational database holding whatever rows are named — or with no payments table
    /// at all — and the provider held behind its latch, as the API registers it.
    /// </summary>
    private static async Task<ServiceProvider> DatabaseHolding(
        IPaymentProvider provider,
        params string[] providersAlreadyPaidThrough) =>
        await DatabaseHolding(provider, dropPayments: false, providersAlreadyPaidThrough);

    private static async Task<ServiceProvider> DatabaseHolding(
        IPaymentProvider provider,
        bool dropPayments,
        params string[] providersAlreadyPaidThrough)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<KhadraDbContext>()
            .UseSqlite(connection)
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(warnings => warnings.Throw(CoreEventId.RowLimitingOperationWithoutOrderByWarning))
            .Options;

        await using (var seed = new KhadraDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            foreach (var name in providersAlreadyPaidThrough)
                seed.Payments.Add(Payment.Open(Id.New(), Id.New(), Money.Jod(75m), name, Build.Now.AddMinutes(30), Build.Now));
            await seed.SaveChangesAsync();
            if (dropPayments)
                await seed.Database.ExecuteSqlRawAsync("DROP TABLE payment_refunds; DROP TABLE payments;");
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<PaymentVerification>();
        services.AddSingleton<DeferredStartupWork>();
        services.AddSingleton<IPaymentProvider>(sp => new VerifiedPaymentProvider(provider, sp.GetRequiredService<PaymentVerification>()));
        services.AddSingleton<IPaymentProviderProbe>(new FixedProbe(provider.Name));
        services.AddScoped(_ => new KhadraDbContext(options));
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// A container where the boot put the bootstrap off and payments are already settled, so only the bootstrap is
    /// left: the real <see cref="AdminBootstrapper"/> over substituted repositories, as <c>AdminBootstrapTests</c> builds it.
    /// </summary>
    private static (ServiceProvider Services, IUserRepository Users) BootstrapPutOff()
    {
        var users = Substitute.For<IUserRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        var composer = Substitute.For<IAuthEmailComposer>();
        composer.AdminInvitation(Arg.Any<User>(), Arg.Any<string>())
            .Returns(new EmailMessage(BootstrapSettings.Address, "Name", "Subject", "<p>html</p>", "text"));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<PaymentVerification>();
        services.AddSingleton<DeferredStartupWork>();
        services.AddScoped(_ => new AdminBootstrapper(
            users,
            Substitute.For<IVerificationTokenRepository>(),
            new FakePasswordHasher(),
            new FakeOpaqueTokens(),
            TestAuthPolicy.Default,
            new BootstrapSettings(),
            new AuthEmailDispatcher(composer, TestEmail.AcceptingSender(), NullLogger<AuthEmailDispatcher>.Instance),
            Substitute.For<IAuditTrail>(),
            unitOfWork,
            new TestClock(Build.Now),
            NullLogger<AdminBootstrapper>.Instance));
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<PaymentVerification>().MarkVerified();
        provider.GetRequiredService<DeferredStartupWork>().DeferAdminBootstrap();
        return (provider, users);
    }

    private sealed class BootstrapSettings : IAdminBootstrapSettings
    {
        public const string Address = "founder@khadra.jo";

        public string? Email => Address;

        public string? Phone => "0790000001";

        public string? FullName => "Rania Haddad";
    }

    private static DeferredStartupService Deferred(IServiceProvider services, IHostApplicationLifetime lifetime) =>
        new(
            services.GetRequiredService<IServiceScopeFactory>(),
            services.GetRequiredService<PaymentVerification>(),
            services.GetRequiredService<DeferredStartupWork>(),
            lifetime,
            NullLogger<DeferredStartupService>.Instance);

    private static SandboxPaymentProvider Sandbox() =>
        new(Options.Create(new PaymentOptions
        {
            Provider = PaymentOptions.SandboxProvider,
            WebhookSecret = Secret,
            SandboxConsoleBaseUrl = ConsoleBaseUrl,
        }),
        new TestClock(Build.Now));

    private sealed class FixedProbe(string detail) : IPaymentProviderProbe
    {
        public Task<string> DescribeAsync(CancellationToken cancellationToken = default) => Task.FromResult(detail);
    }

    /// <summary>A real provider, as far as the guard can tell: it moves real money and is configured.</summary>
    private sealed class LiveStub : IPaymentProvider
    {
        public string Name => "HyperPay";

        public PaymentMode Mode => PaymentMode.Live;

        public Task<Result<CheckoutSession, Error>> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Result<ProviderEvent, Error> ParseEvent(string rawBody, IReadOnlyDictionary<string, string> headers) =>
            throw new NotSupportedException();

        public Task<Result<ProviderPaymentState, Error>> QueryAsync(string providerReference, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<ProviderRefund, Error>> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
