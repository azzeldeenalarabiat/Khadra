using System.ComponentModel.DataAnnotations;

namespace Khadra.Infrastructure.Configuration;

public sealed class EmailOptions
{
    public const string SectionName = "Email";
    public const string SmtpProvider = "Smtp";
    public const string LoggingProvider = "Logging";

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

    [Required]
    public string FromAddress { get; init; } = "no-reply@khadra.local";

    [Required]
    public string FromName { get; init; } = "Khadra";
}
