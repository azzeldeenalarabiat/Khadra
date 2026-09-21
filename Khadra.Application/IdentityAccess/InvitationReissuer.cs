using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;

namespace Khadra.Application.IdentityAccess;

/// <summary>
/// A fresh invitation link for somebody who has not accepted theirs, of whatever kind.
///
/// Two things, in this order and never one without the other: every live token for that person and
/// purpose is invalidated, then one new token is issued. A reissue that only ADDED would leave two
/// working links to the same account, and the older one is the one most likely to be in a place
/// nobody controls — a log, a forwarded email, an inbox on a lost phone.
/// </summary>
/// <remarks>
/// The caller commits. This STAGES the invalidation and the new token so they land in the same
/// transaction as whatever else the handler is recording, which for an administrator is the audit
/// entry that says a second key was cut.
///
/// The caller also sends. Nothing here touches email: the token has to be committed before a link
/// to it is posted, and only the caller knows whether a relay that refuses should fail the command.
/// </remarks>
public sealed class InvitationReissuer(
    IVerificationTokenRepository verificationTokens,
    IOpaqueTokenService opaqueTokens,
    IAuthPolicySettings policy,
    IClock clock)
{
    /// <returns>The RAW token for the email, and when the link stops working.</returns>
    public async Task<(string RawToken, DateTimeOffset ExpiresAt)> ReissueAsync(
        User user,
        VerificationPurpose purpose,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(purpose);

        var now = clock.UtcNow;
        var lifetime = policy.EmployeeInvitationLifetime;

        await verificationTokens.InvalidateActiveAsync(user.Id, purpose, now, cancellationToken);

        var invitation = opaqueTokens.Generate();
        await verificationTokens.AddAsync(
            VerificationToken.Issue(user.Id, purpose, invitation.Hash, now, lifetime),
            cancellationToken);

        return (invitation.Value, now.Add(lifetime));
    }
}
