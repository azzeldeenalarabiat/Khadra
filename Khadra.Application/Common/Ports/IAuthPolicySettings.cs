namespace Khadra.Application.Common.Ports;

// Token lifetimes and password rules. Bound from configuration by Infrastructure; never hardcoded here.
public interface IAuthPolicySettings
{
    TimeSpan RefreshTokenLifetime { get; }

    // Absolute ceiling for a login session regardless of how many times it is refreshed.
    TimeSpan RefreshFamilyLifetime { get; }

    TimeSpan EmailVerificationLifetime { get; }

    TimeSpan PasswordResetLifetime { get; }

    // Days, not minutes: an invited employee reads the email when they next look at their inbox,
    // which is not the same moment they asked for a password reset.
    TimeSpan EmployeeInvitationLifetime { get; }

    int PasswordMinimumLength { get; }
}
