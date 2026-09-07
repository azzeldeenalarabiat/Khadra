using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.IdentityAccess.Dtos;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using MediatR;

namespace Khadra.Application.IdentityAccess.RefreshTokens;

/// <summary>
/// Rotates a refresh token, and tells a retry apart from a replay.
/// </summary>
/// <remarks>
/// Rotation is single-use: presenting a consumed token normally means someone else has a copy, and
/// the whole family dies. That is right, and on a phone it fires constantly on something that is not
/// an attack at all — the request arrives, the rotation commits, and the RESPONSE is lost to a radio
/// handover, a tunnel, or iOS suspending the app mid-flight. The customer still holds the old token,
/// their next attempt looks exactly like a replay, and they are signed out of a working session for
/// a network hiccup. On mobile that is the common case, not the exotic one.
///
/// So a consumed token presented WITHIN <see cref="IAuthPolicySettings.RefreshReuseGrace"/>, whose
/// replacement has itself never been used, is treated as the retry it is: the unused replacement is
/// retired and a fresh pair issued. Past the window, or once the replacement has been used, nothing
/// is softened — the family dies exactly as before. The narrowing that matters is "never used": if
/// anyone has spent the replacement, two parties hold live tokens and that IS the attack.
/// </remarks>
public sealed class RefreshTokensHandler(
    IRefreshTokenRepository refreshTokens,
    IUserRepository users,
    IOpaqueTokenService opaqueTokens,
    AuthTokenFactory tokenFactory,
    IAuthPolicySettings policy,
    IClock clock,
    IUnitOfWork unitOfWork)
    : IRequestHandler<RefreshTokensCommand, Result<AuthTokensDto, Error>>
{
    public async Task<Result<AuthTokensDto, Error>> Handle(
        RefreshTokensCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = clock.UtcNow;
        var current = await refreshTokens.GetByHashAsync(opaqueTokens.Hash(request.RefreshToken), cancellationToken);
        if (current is null)
            return IdentityErrors.InvalidRefreshToken;

        if (current.IsRevoked)
            return await RetryOrRevokeFamilyAsync(current, request, now, cancellationToken);

        if (current.IsExpired(now))
            return IdentityErrors.InvalidRefreshToken;

        return await IssueAsync(current, request, now, cancellationToken);
    }

    /// <summary>
    /// A consumed token was presented. Decide whether that is a lost response or a replay.
    /// </summary>
    private async Task<Result<AuthTokensDto, Error>> RetryOrRevokeFamilyAsync(
        RefreshToken current,
        RefreshTokensCommand request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var replacement = await FindUnusedReplacementAsync(current, now, cancellationToken);
        if (replacement is null)
        {
            // Replay, or a retry that arrived too late to tell apart from one.
            await refreshTokens.RevokeFamilyAsync(current.FamilyId, now, cancellationToken);
            return IdentityErrors.InvalidRefreshToken;
        }

        // The customer never received the replacement, so it is retired unused and a fresh pair
        // issued from it. The family is not extended: the replacement carries the same absolute
        // deadline, and so does everything issued from it.
        return await IssueAsync(replacement, request, now, cancellationToken);
    }

    /// <summary>
    /// How far down a chain of lost responses this will follow before deciding it is looking at an
    /// attack rather than a bad connection.
    /// </summary>
    /// <remarks>
    /// More than one, because a customer on a failing connection retries and can lose that response
    /// too — signing them out on the second hiccup is the same mistake as signing them out on the
    /// first. Bounded, because the walk is over rows an attacker could otherwise make arbitrarily
    /// long, and because a genuine client cannot need many: each hop is a whole request that
    /// reached the server and came back to nobody.
    /// </remarks>
    private const int MaxLostResponsesFollowed = 5;

    /// <summary>
    /// The live token at the end of this one's replacement chain, if every hop along the way looks
    /// like a response that never arrived.
    /// </summary>
    private async Task<RefreshToken?> FindUnusedReplacementAsync(
        RefreshToken current,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (policy.RefreshReuseGrace <= TimeSpan.Zero)
            return null;

        var token = current;
        for (var hop = 0; hop < MaxLostResponsesFollowed; hop++)
        {
            // Revoked long ago is a replay, not a retry: a client whose response was lost comes
            // back within seconds, not hours.
            if (token.RevokedAt is not { } revokedAt || now - revokedAt > policy.RefreshReuseGrace)
                return null;
            // Revoked by a logout or a family kill rather than by rotation: nothing to hand back.
            if (token.ReplacedByTokenId is not { } replacementId)
                return null;

            var replacement = await refreshTokens.GetByIdAsync(replacementId, cancellationToken);
            if (replacement is null || replacement.IsExpired(now))
                return null;

            // Never used: this is the one the customer should have had.
            if (!replacement.IsRevoked)
                return replacement;

            // Used, but only just — another lost response. Keep walking.
            token = replacement;
        }

        return null;
    }

    private async Task<Result<AuthTokensDto, Error>> IssueAsync(
        RefreshToken from,
        RefreshTokensCommand request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(from.UserId, cancellationToken);
        if (user is null)
            return IdentityErrors.InvalidRefreshToken;

        var canAuthenticate = user.CanAuthenticate();
        if (canAuthenticate.IsFailure)
            return canAuthenticate.Error;

        var tokens = await tokenFactory.IssueReplacementAsync(user, from, request.Client, now, cancellationToken);
        if (tokens.IsFailure)
            return tokens.Error;

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // Two refreshes raced on one token and this one lost. The winner committed first and
            // holds a perfectly good replacement — killing the family here would destroy it, and
            // punish the customer for a race their own client caused. The loser is simply refused;
            // its client retries and, inside the grace above, gets the winner's replacement handed
            // to it.
            return IdentityErrors.InvalidRefreshToken;
        }

        return tokens.Value;
    }
}
