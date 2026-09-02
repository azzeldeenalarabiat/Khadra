using System.ComponentModel.DataAnnotations;

namespace Khadra.Infrastructure.Configuration;

public sealed class AppOptions
{
    public const string SectionName = "App";

    // Base URL of the client that renders /verify-email and /reset-password (dashboard through the BFF).
    [Required, Url]
    public string ClientBaseUrl { get; init; } = "https://localhost:7243";

    // Browser origins allowed to call the API directly (normally only the BFF talks to the API).
    public string[] AllowedOrigins { get; init; } = [];
}
