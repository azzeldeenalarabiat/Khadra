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

    // Seconds. Long enough to cover a lost response and the customer's next attempt, short enough
    // that a stolen token is worth very little. Zero disables the grace entirely and restores strict
    // single-use rotation.
    [Range(0, 300)]
    public int RefreshReuseGraceSeconds { get; init; } = 60;

    [Range(1, 168)]
    public int EmailVerificationHours { get; init; } = 24;

    [Range(5, 1440)]
    public int PasswordResetMinutes { get; init; } = 60;

    // How long a dealer's invitation to a member of staff stays valid (spec 4.2).
    [Range(1, 30)]
    public int EmployeeInvitationDays { get; init; } = 7;

    [Range(8, 64)]
    public int PasswordMinimumLength { get; init; } = 8;

    [Range(10, 15)]
    public int BcryptWorkFactor { get; init; } = 12;
}
