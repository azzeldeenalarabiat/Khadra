using System.ComponentModel.DataAnnotations;

namespace Khadra.Infrastructure.Configuration;

public sealed class JwtOptions
{
    public const string SectionName = "Authentication:Jwt";

    [Required]
    public string Issuer { get; init; } = "Khadra";

    [Required]
    public string Audience { get; init; } = "khadra-api";

    // Secret only: user-secrets / env var Authentication__Jwt__SigningKey. Validated for length on start.
    [Required, MinLength(32)]
    public string SigningKey { get; init; } = null!;

    [Range(5, 60)]
    public int AccessTokenMinutes { get; init; } = 15;
}
