using System.ComponentModel.DataAnnotations;

namespace Khadra.Infrastructure.Configuration;

public sealed class EmailOptions
{
    public const string SectionName = "Email";
    public const string SmtpProvider = "Smtp";
    public const string LoggingProvider = "Logging";
    /// <summary>Resend’s HTTPS API. Needs only <see cref="ApiKey"/> and a From address.</summary>
    public const string ResendProvider = "Resend";

    // "Smtp" (Mailpit in dev, a real relay in production) or "Logging" (writes the mail to the log).
    [Required]
    public string Provider { get; init; } = LoggingProvider;

    public string Host { get; init; } = "localhost";

    [Range(1, 65535)]
    public int Port { get; init; } = 1025;

    public bool UseStartTls { get; init; }

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
