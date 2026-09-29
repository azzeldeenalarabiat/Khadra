using Khadra.Application.Common.Ports;

namespace Khadra.Tests.Support;

/// <summary>
/// A mail transport that keeps what it is handed (payments Phase 7): every message, attachments and idempotency key
/// included — or refuses, as a transport that is down or says no does.
/// </summary>
internal sealed class RecordingEmailSender : IEmailSender
{
    private int _sent;

    public List<EmailMessage> Messages { get; } = [];

    /// <summary>When set, every send throws it, as a refusing or unreachable transport does.</summary>
    public Exception? Refusal { get; set; }

    /// <summary>
    /// Runs as each message is handed over, before anything else — to stop the process mid-send, or to let another
    /// process in while this one is sending. Whatever it throws, the send throws.
    /// </summary>
    public Func<EmailMessage, Task>? OnSend { get; set; }

    /// <summary>The transport's name on its receipts.</summary>
    public string Provider { get; set; } = "Smtp";

    public async Task<EmailSendReceipt> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (OnSend is { } onSend)
            await onSend(message);
        if (Refusal is { } refusal)
            throw refusal;

        Messages.Add(message);
        _sent++;
        return new EmailSendReceipt(
            Provider,
            $"<test-{_sent}@khadra.test>",
            DateTimeOffset.UnixEpoch,
            Attempts: 1,
            ProviderResponse: null);
    }
}

/// <summary>The document emails' mechanics, set by the test (payments Phase 7).</summary>
internal sealed class TestDocumentEmailSettings : Khadra.Application.FinancialDocuments.Email.IFinancialDocumentEmailSettings
{
    public int BatchSize { get; set; } = 10;

    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(5);

    public int MaxSendAttempts { get; set; } = 3;

    public TimeSpan RetryDelay(int sendAttempt) => TimeSpan.FromMinutes(sendAttempt);

    public TimeSpan PdfWaitDelay { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromMinutes(30);

    public string? CustomerBaseUrl { get; set; }

    public string TransportName { get; set; } = "Smtp";

    public bool TransportDeliversMail { get; set; } = true;

    /// <summary>True, as the local stack's Mailpit is: every TEST receipt may go. A test of a real provider sets it false.</summary>
    public bool TransportCapturesMail { get; set; } = true;

    /// <summary>The test-recipient allowlist, compared ignoring case as the real one is.</summary>
    public HashSet<string> TestRecipients { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsTestRecipient(string address) => TestRecipients.Contains(address);

    /// <summary>The test issuer's support address, where the real settings take the configured issuer's.</summary>
    public string? ReplyTo { get; set; } = DocumentFixtures.Issuer.SupportEmail;

    /// <summary>Null: this host may send. A test of Production on Brevo sets the reason the real settings give.</summary>
    public string? DeliveryDisabledReason { get; set; }
}
