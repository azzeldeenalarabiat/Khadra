namespace Khadra.Application.Common.Ports;

// Token lifetimes and password rules. Bound from configuration by Infrastructure; never hardcoded here.
public interface IAuthPolicySettings
{
    TimeSpan RefreshTokenLifetime { get; }

    // Absolute ceiling for a login session regardless of how many times it is refreshed.
    TimeSpan RefreshFamilyLifetime { get; }

    /// <summary>
    /// How long after a token was rotated its presentation is still treated as a retry rather than
    /// a replay.
    /// </summary>
    /// <remarks>
    /// Rotation is single-use, and presenting a consumed token normally means the family is
    /// compromised. On a phone it usually means something far duller: the request arrived, the
    /// rotation committed, and the RESPONSE was lost to a radio handover or the app being suspended.
    /// The customer still holds the old token, and their next refresh looks exactly like an attack.
    ///
    /// Inside this window, and only while the replacement has never itself been used, that is
    /// treated as the retry it is. Outside it, or once the replacement has been used, replay
    /// detection is unchanged.
    /// </remarks>
    TimeSpan RefreshReuseGrace { get; }

    TimeSpan EmailVerificationLifetime { get; }

    TimeSpan PasswordResetLifetime { get; }

    // Days, not minutes: an invited employee reads the email when they next look at their inbox,
    // which is not the same moment they asked for a password reset.
    TimeSpan EmployeeInvitationLifetime { get; }

    int PasswordMinimumLength { get; }
}
