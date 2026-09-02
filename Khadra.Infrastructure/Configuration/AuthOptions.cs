using System.ComponentModel.DataAnnotations;

namespace Khadra.Infrastructure.Configuration;

public sealed class AuthOptions
{
    public const string SectionName = "Authentication:Policy";

    [Range(1, 90)]
    public int RefreshTokenDays { get; init; } = 14;

    // Absolute session ceiling regardless of refreshes.
    [Range(1, 365)]
    public int RefreshFamilyDays { get; init; } = 30;

    [Range(1, 168)]
    public int EmailVerificationHours { get; init; } = 24;

    [Range(5, 1440)]
    public int PasswordResetMinutes { get; init; } = 60;

    [Range(8, 64)]
    public int PasswordMinimumLength { get; init; } = 8;

    [Range(10, 15)]
    public int BcryptWorkFactor { get; init; } = 12;
}
