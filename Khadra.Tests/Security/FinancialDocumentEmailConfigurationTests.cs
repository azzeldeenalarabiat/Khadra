using Khadra.Infrastructure;
using Khadra.Infrastructure.Configuration;
using Khadra.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Khadra.Tests.Security;

/// <summary>
/// How the receipts' emails are worked (payments Phase 7), as the shipped settings file carries it and as startup
/// refuses it — and where the owner's rules of 2026-09-29 meet configuration: a TEST receipt reaches a real inbox only
/// through the allowlist, only the local Mailpit counts as a server that delivers nowhere, and replies go to Khadra's
/// support address.
/// </summary>
/// <remarks>
/// Built from the REAL <c>Khadra.WebAPI/appsettings.json</c>, with one key changed per case, so each test proves both
/// that the shipped file is valid and that the change alone is what startup refuses.
/// </remarks>
public sealed class FinancialDocumentEmailConfigurationTests
{
    private static FinancialDocumentEmailOptions Bound(params (string Key, string? Value)[] overrides)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=khadra_tests;Username=x;Password=y",
            ["Authentication:Jwt:SigningKey"] = new string('k', 48),
        };
        foreach (var (key, value) in overrides)
            settings[key] = value;

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(RepositoryRoot.File("Khadra.WebAPI", "appsettings.json"))
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(
            new Microsoft.Extensions.Hosting.Internal.HostingEnvironment
            {
                ContentRootPath = Path.GetTempPath(),
                EnvironmentName = "Testing",
                ApplicationName = "Khadra.Tests",
            });
        services.AddInfrastructureServices(configuration);
        return services.BuildServiceProvider().GetRequiredService<IOptions<FinancialDocumentEmailOptions>>().Value;
    }

    private static FinancialDocumentEmailSettings Settings(
        EmailOptions? email = null,
        FinancialDocumentEmailOptions? emails = null,
        FinancialDocumentIssuerOptions? issuer = null,
        string environment = "Development") =>
        new(
            Options.Create(emails ?? new FinancialDocumentEmailOptions()),
            Options.Create(email ?? new EmailOptions()),
            Options.Create(new AppOptions()),
            Options.Create(new FinancialDocumentOptions { Issuer = issuer ?? new FinancialDocumentIssuerOptions() }),
            new Microsoft.Extensions.Hosting.Internal.HostingEnvironment
            {
                EnvironmentName = environment,
                ApplicationName = "Khadra.Tests",
                ContentRootPath = Path.GetTempPath(),
            });

    [Fact]
    public void The_shipped_settings_hold_a_send_well_inside_its_lease_and_allow_no_test_recipient()
    {
        var options = Bound();

        Assert.Equal(300, options.LeaseSeconds);
        Assert.True(options.LeaseOutlastsSend(15));
        Assert.Empty(options.TestRecipients);
    }

    [Fact]
    public void A_lease_shorter_than_twice_the_transports_budget_is_refused_at_startup()
    {
        // A mail timeout raised to its maximum would otherwise outlive the lease renewed for the send.
        var failure = Assert.Throws<OptionsValidationException>(() => Bound(("Email:TimeoutSeconds", "300")));
        Assert.Contains("LeaseSeconds must be at least twice Email:TimeoutSeconds", failure.Message, StringComparison.Ordinal);

        Assert.Equal(600, Bound(("Email:TimeoutSeconds", "300"), ("FinancialDocuments:Email:LeaseSeconds", "600")).LeaseSeconds);
    }

    [Fact]
    public void A_retry_cap_below_the_first_retry_is_refused_at_startup()
    {
        var failure = Assert.Throws<OptionsValidationException>(() => Bound(("FinancialDocuments:Email:RetryCapSeconds", "30")));
        Assert.Contains("RetryCapSeconds must be at least RetryBaseSeconds", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_test_recipient_allowlist_names_addresses_and_nothing_else()
    {
        var bound = Bound(("FinancialDocuments:Email:TestRecipients:0", "owner@khadra.jo"), ("FinancialDocuments:Email:TestRecipients:1", "qa@khadra.jo"));
        Assert.Equal(["owner@khadra.jo", "qa@khadra.jo"], bound.TestRecipients);

        var failure = Assert.Throws<OptionsValidationException>(() => Bound(("FinancialDocuments:Email:TestRecipients:0", "everyone at khadra")));
        Assert.Contains("TestRecipients: every entry must be an email address", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_allowlisted_address_is_recognised_ignoring_case_and_nothing_else_is()
    {
        var settings = Settings(emails: new FinancialDocumentEmailOptions { TestRecipients = [" Owner@Khadra.jo "] });

        Assert.True(settings.IsTestRecipient("owner@khadra.jo"));
        Assert.False(settings.IsTestRecipient("rana@example.jo"));
        Assert.False(settings.IsTestRecipient(""));
        Assert.False(Settings().IsTestRecipient("owner@khadra.jo"));
    }

    [Theory]
    [InlineData("Smtp", "localhost", 1025, true)]
    [InlineData("Smtp", "127.0.0.1", 1025, true)]
    [InlineData("Smtp", "::1", 1025, true)]
    [InlineData("Smtp", "mailpit", 1025, true)]
    // A relay on this machine that is not Mailpit, and real relays anywhere, could reach a real inbox.
    [InlineData("Smtp", "localhost", 587, false)]
    [InlineData("Smtp", "localhost", 25, false)]
    [InlineData("Smtp", "smtp-relay.brevo.com", 1025, false)]
    [InlineData("Smtp", "smtp.gmail.com", 587, false)]
    [InlineData("Smtp", "", 1025, false)]
    [InlineData("Brevo", "localhost", 1025, false)]
    [InlineData("Resend", "localhost", 1025, false)]
    public void Only_the_local_mailpit_counts_as_a_server_that_delivers_nowhere(string provider, string host, int port, bool captures)
    {
        var settings = Settings(email: new EmailOptions { Provider = provider, Host = host, Port = port });

        Assert.Equal(captures, settings.TransportCapturesMail);
        Assert.True(settings.TransportDeliversMail);
    }

    [Theory]
    // Production on Brevo — by its API or by its SMTP relay, the same provider — sends no financial-document email.
    [InlineData("Production", "Brevo", "", true)]
    [InlineData("Production", "Smtp", "smtp-relay.brevo.com", true)]
    [InlineData("Production", "Smtp", "SMTP-RELAY.BREVO.COM.", true)]
    [InlineData("Production", "Smtp", "smtp-relay.sendinblue.com", true)]
    // Providers with proven idempotency, and every other transport, are unaffected; a lookalike host is not Brevo.
    [InlineData("Production", "Resend", "", false)]
    [InlineData("Production", "Smtp", "smtp.gmail.com", false)]
    [InlineData("Production", "Smtp", "notbrevo.com", false)]
    // Local and Staging may use Brevo, under the TEST allowlist.
    [InlineData("Staging", "Brevo", "", false)]
    [InlineData("Development", "Brevo", "", false)]
    [InlineData("Development", "Smtp", "smtp-relay.brevo.com", false)]
    public void Only_production_on_brevo_switches_financial_document_emails_off(string environment, string provider, string host, bool off)
    {
        var settings = Settings(email: new EmailOptions { Provider = provider, Host = host, Port = 587 }, environment: environment);

        Assert.Equal(off ? FinancialDocumentEmailSettings.BrevoInProduction : null, settings.DeliveryDisabledReason);
    }

    [Fact]
    public void The_reason_names_the_provider_the_unverified_idempotency_and_the_item_and_says_nothing_is_lost()
    {
        var reason = Settings(email: new EmailOptions { Provider = "Brevo" }, environment: "Production").DeliveryDisabledReason!;

        Assert.Contains("this is Production and the mail provider is Brevo", reason, StringComparison.Ordinal);
        Assert.Contains("single-send idempotency has not been verified (pre-launch item 202)", reason, StringComparison.Ordinal);
        Assert.Contains("their emails wait in the queue", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Replies_go_to_the_configured_issuers_support_address_and_nowhere_without_one()
    {
        var issuer = new FinancialDocumentIssuerOptions
        {
            LegalNameEn = "Khadra",
            LegalNameAr = "خضرا",
            CommercialRegistration = "123456",
            AddressEn = "Amman",
            AddressAr = "عمّان",
            SupportEmail = "  support@khadra.jo ",
            SupportPhone = "+962 6 000 0000",
        };

        Assert.Equal("support@khadra.jo", Settings(issuer: issuer).ReplyTo);
        Assert.Null(Settings().ReplyTo);
    }
}
