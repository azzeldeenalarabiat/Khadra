using System.ComponentModel.DataAnnotations;
using System.Net;
using Khadra.Application.FinancialDocuments.Email;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Configuration;

/// <summary>
/// How issued receipts are emailed (payments Phase 7; section <c>FinancialDocuments:Email</c>). Mechanics of a
/// sender, not business rules: which documents are emailed, and that each carries its PDF, are the owner's and live
/// in the domain (<c>FinancialDocumentType.IsEmailedToCustomer</c>).
/// </summary>
public sealed class FinancialDocumentEmailOptions
{
    public const string SectionName = "FinancialDocuments:Email";

    [Range(1, 100)]
    public int BatchSize { get; init; } = 10;

    /// <summary>
    /// How long a taken email is held from other processes. Each send renews it just before the transport is called,
    /// so it must outlast one send — startup refuses less than twice <c>Email:TimeoutSeconds</c>, the transport's whole
    /// budget. A batch that outlives the claim is safe: a row claimed again meanwhile is left to the process that claimed
    /// it (its claim count moved), so this only decides how soon a dead process's emails go out.
    /// </summary>
    [Range(30, 3600)]
    public int LeaseSeconds { get; init; } = 300;

    /// <summary>Whether the lease renewed for a send outlasts it, with the transport's budget twice over.</summary>
    public bool LeaseOutlastsSend(int transportTimeoutSeconds) => LeaseSeconds >= 2 * transportTimeoutSeconds;

    [Range(1, 20)]
    public int MaxSendAttempts { get; init; } = 5;

    /// <summary>The first retry waits this long; each after it twice as long, up to the cap.</summary>
    [Range(1, 3600)]
    public int RetryBaseSeconds { get; init; } = 60;

    [Range(1, 86400)]
    public int RetryCapSeconds { get; init; } = 3600;

    /// <summary>How long a queued email waits before it looks again for a PDF that is not drawn yet.</summary>
    [Range(5, 3600)]
    public int PdfWaitSeconds { get; init; } = 60;

    /// <summary>How long an email may stay queued before the administrator's work queue lists it. PDFs follow issue within a minute.</summary>
    [Range(1, 10080)]
    public int StaleAfterMinutes { get; init; } = 30;

    /// <summary>
    /// The only addresses a TEST document is emailed to through a real mail provider (owner, 2026-09-29), so a sandbox
    /// receipt never reaches an arbitrary inbox. Empty by default: a real provider then sends TEST receipts to nobody,
    /// while the local stack's Mailpit, which delivers nowhere, needs none. Compared ignoring case.
    /// </summary>
    public IReadOnlyList<string> TestRecipients { get; init; } = [];

    /// <summary>Every allowlisted entry is an address, never a blank or a pattern.</summary>
    public bool TestRecipientsAreAddresses =>
        TestRecipients.All(address => !string.IsNullOrWhiteSpace(address) && new EmailAddressAttribute().IsValid(address.Trim()));
}

internal sealed class FinancialDocumentEmailSettings(
    IOptions<FinancialDocumentEmailOptions> options,
    IOptions<EmailOptions> email,
    IOptions<AppOptions> app,
    IOptions<FinancialDocumentOptions> documents,
    IHostEnvironment environment) : IFinancialDocumentEmailSettings
{
    /// <summary>Brevo's own domains, the old name included: its SMTP relay is <c>smtp-relay.brevo.com</c>.</summary>
    private static readonly string[] BrevoDomains = ["brevo.com", "sendinblue.com"];

    /// <summary>Said in the boot log and beside a refused request's log line; never to a customer.</summary>
    internal const string BrevoInProduction =
        "Financial-document email delivery is disabled: this is Production and the mail provider is Brevo, whose single-send idempotency has not been verified (pre-launch item 202). Receipts are issued and stay in the customer's account; their emails wait in the queue until a provider with proven idempotency is configured.";

    /// <summary>Mailpit's SMTP port, as the local stack runs it (<c>docker-compose.yml</c>, <c>start-customer-web.ps1</c>).</summary>
    internal const int MailpitSmtpPort = 1025;

    public int BatchSize => options.Value.BatchSize;

    public TimeSpan Lease => TimeSpan.FromSeconds(options.Value.LeaseSeconds);

    public int MaxSendAttempts => options.Value.MaxSendAttempts;

    public TimeSpan RetryDelay(int sendAttempt)
    {
        var exponent = Math.Clamp(sendAttempt - 1, 0, 20);
        var seconds = Math.Min((double)options.Value.RetryBaseSeconds * Math.Pow(2, exponent), options.Value.RetryCapSeconds);
        return TimeSpan.FromSeconds(seconds);
    }

    public TimeSpan PdfWaitDelay => TimeSpan.FromSeconds(options.Value.PdfWaitSeconds);

    public TimeSpan StaleAfter => TimeSpan.FromMinutes(options.Value.StaleAfterMinutes);

    public string? CustomerBaseUrl => string.IsNullOrWhiteSpace(app.Value.CustomerAppBaseUrl) ? null : app.Value.CustomerAppBaseUrl.Trim();

    // The transport this process actually chose: an empty setting means Logging (see AddEmail).
    public string TransportName
    {
        get
        {
            var provider = email.Value.Provider?.Trim() ?? string.Empty;
            foreach (var known in new[] { EmailOptions.BrevoProvider, EmailOptions.ResendProvider, EmailOptions.SmtpProvider })
            {
                if (string.Equals(provider, known, StringComparison.OrdinalIgnoreCase))
                    return known;
            }

            return EmailOptions.LoggingProvider;
        }
    }

    public bool TransportDeliversMail => TransportName != EmailOptions.LoggingProvider;

    // Mailpit and nothing else: SMTP to its port on this machine, or on the compose network's own `mailpit`. A relay
    // anywhere else — Gmail, Brevo's SMTP relay, any host a staging server names — could reach a real inbox.
    public bool TransportCapturesMail =>
        TransportName == EmailOptions.SmtpProvider && email.Value.Port == MailpitSmtpPort && IsThisMachineOrMailpit(email.Value.Host);

    public bool IsTestRecipient(string address) =>
        !string.IsNullOrWhiteSpace(address)
        && options.Value.TestRecipients.Any(allowed => string.Equals(allowed?.Trim(), address.Trim(), StringComparison.OrdinalIgnoreCase));

    public string? ReplyTo => documents.Value.Issuer is { IsComplete: true } issuer ? issuer.SupportEmail!.Trim() : null;

    // Owner, 2026-09-29: until Brevo's single-send idempotency is verified, a Production host on Brevo sends no
    // financial-document email — by its API, or by its SMTP relay, which is the same provider. Local and Staging may,
    // under the TEST allowlist; Resend and every other transport are unaffected.
    public string? DeliveryDisabledReason => environment.IsProduction() && ProviderIsBrevo ? BrevoInProduction : null;

    internal bool ProviderIsBrevo =>
        TransportName == EmailOptions.BrevoProvider
        || (TransportName == EmailOptions.SmtpProvider && IsBrevoRelay(email.Value.Host));

    internal static bool IsBrevoRelay(string? host)
    {
        var name = host?.Trim().TrimEnd('.');
        if (string.IsNullOrEmpty(name))
            return false;
        return BrevoDomains.Any(domain =>
            string.Equals(name, domain, StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsThisMachineOrMailpit(string? host)
    {
        var name = host?.Trim();
        if (string.IsNullOrEmpty(name))
            return false;
        if (string.Equals(name, "localhost", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "mailpit", StringComparison.OrdinalIgnoreCase))
            return true;
        return IPAddress.TryParse(name.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address);
    }
}
