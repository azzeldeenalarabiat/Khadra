using System.ComponentModel.DataAnnotations;

namespace Khadra.Infrastructure.Configuration;

public sealed class EmailOptions
{
    public const string SectionName = "Email";
    public const string SmtpProvider = "Smtp";
    public const string LoggingProvider = "Logging";
    /// <summary>Resend’s HTTPS API. Needs only <see cref="ApiKey"/> and a From address.</summary>
    public const string ResendProvider = "Resend";

    /// <summary>
    /// Brevo’s HTTPS API. Same account and sender as the SMTP relay, over 443 instead of 587.
    /// </summary>
    /// <remarks>
    /// The one to reach for when the relay is unreachable but recipients are arbitrary: Resend’s free
    /// tier writes only to the account owner until a domain is verified, while Brevo asks only that
    /// the sender is confirmed. Needs the <c>xkeysib-</c> API key, NOT the <c>xsmtpsib-</c> SMTP one.
    /// </remarks>
    public const string BrevoProvider = "Brevo";

    // "Smtp" (Mailpit in dev, a real relay in production) or "Logging" (writes the mail to the log).
    [Required]
    public string Provider { get; init; } = LoggingProvider;

    public string Host { get; init; } = "localhost";

    [Range(1, 65535)]
    public int Port { get; init; } = 1025;

    public bool UseStartTls { get; init; }

    /// <summary>
    /// The TOTAL time a caller may wait on SMTP, in seconds, across every attempt.
    /// </summary>
    /// <remarks>
    /// A registration WAITS on the verification mail, so this is the longest a gallery owner can be
    /// left watching the form. MailKit's own default is two minutes, which on a network that drops
    /// SMTP payload silently -- a hotel, a phone hotspot, a carrier that throttles port 587 -- means
    /// the account is created and the screen says nothing for two minutes before admitting the mail
    /// never went. Fifteen seconds is generous for a relay that is answering, and quick to give up
    /// on one that is not. The Resend transport already caps its HTTP call the same way.
    /// </remarks>
    [Range(1, 300)]
    public int TimeoutSeconds { get; init; } = 15;

    /// <summary>How many times to try one message before giving up.</summary>
    /// <remarks>
    /// A relay that drops the connection, or accepts the TCP handshake and then never sends its
    /// greeting, is a TRANSIENT failure and the commonest one in the wild -- a throttling carrier, a
    /// hotspot, a relay shedding load. One attempt turns that into a lost password-reset link.
    ///
    /// The attempts share <see cref="TimeoutSeconds"/> rather than each getting it, so raising this
    /// never lengthens the wait a person sees; it only spends the same budget on more chances. A
    /// healthy relay answers in about a second and a half, so three attempts inside fifteen seconds
    /// is five seconds each -- generous for a server that is working, quick to abandon one that is
    /// not. Permanent refusals (bad credentials, a rejected sender or recipient) are NOT retried:
    /// the answer would be the same every time.
    ///
    /// This is a stopgap for a single request, not a queue. Mail that fails all attempts is still
    /// lost -- see the pre-launch checklist for the outbox that would fix it properly.
    /// </remarks>
    [Range(1, 5)]
    public int MaxAttempts { get; init; } = 3;

    public string? Username { get; init; }

    // Secret only: user-secrets / env var Email__Password.
    public string? Password { get; init; }

    /// <summary>
    /// The API key for a provider that authenticates over HTTPS rather than SMTP (Resend).
    /// </summary>
    /// <remarks>
    /// Secret only: user-secrets / env var <c>Email__ApiKey</c>. Never a tracked file. Kept separate
    /// from <see cref="Password"/> because they are different credentials for different transports,
    /// and reusing one field would make a half-switched configuration look complete.
    /// </remarks>
    public string? ApiKey { get; init; }

    // Optional, and empty by default. Gmail and most relays refuse a From that is not the account
    // that authenticated, so when this is blank the sender uses Username — which makes a working
    // setup two secrets and nothing else. Set it explicitly only where the relay allows it.
    public string? FromAddress { get; init; }

    [Required]
    public string FromName { get; init; } = "Khadra";
}
