using Khadra.Domain.Bookings;
using Khadra.Domain.Common;

namespace Khadra.Application.Bookings.Handover;

/// <summary>Mints and checks handover codes. The only place a code exists in the clear.</summary>
public interface IHandoverCodeService
{
    /// <summary>A fresh six-digit code from a cryptographic source.</summary>
    string Generate();

    /// <summary>The keyed hash stored for a code: bound to this booking and this handover.</summary>
    string Hash(Id bookingId, HandoverType type, string code);

    /// <summary>Whether a presented code hashes to the stored one. Fixed-time.</summary>
    bool Matches(string storedHash, Id bookingId, HandoverType type, string presentedCode);
}

/// <summary>How handovers are verified. Configuration section <c>Handover</c>.</summary>
public interface IHandoverSettings
{
    /// <summary>
    /// Whether every handover must be proved by a code or recorded as unverified with a reason. Off
    /// until the customer app that shows codes is published; see docs/production.md.
    /// </summary>
    bool RequireVerification { get; }

    TimeSpan CodeLifetime { get; }

    int MaxFailedAttempts { get; }
}
