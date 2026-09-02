namespace Khadra.Application.Common.Ports;

// Token lifetimes and password rules. Bound from configuration by Infrastructure; never hardcoded here.
public interface IAuthPolicySettings
{
    TimeSpan RefreshTokenLifetime { get; }

    // Absolute ceiling for a login session regardless of how many times it is refreshed.
    TimeSpan RefreshFamilyLifetime { get; }

    TimeSpan EmailVerificationLifetime { get; }

    TimeSpan PasswordResetLifetime { get; }

    int PasswordMinimumLength { get; }
}
