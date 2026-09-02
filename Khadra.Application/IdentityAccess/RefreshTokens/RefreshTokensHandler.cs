using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.IdentityAccess.Dtos;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using MediatR;

namespace Khadra.Application.IdentityAccess.RefreshTokens;

public sealed class RefreshTokensHandler(
    IRefreshTokenRepository refreshTokens,
    IUserRepository users,
    IOpaqueTokenService opaqueTokens,
    AuthTokenFactory tokenFactory,
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
        {
            // Replay of an already-rotated token: the family is compromised, kill every descendant.
            await refreshTokens.RevokeFamilyAsync(current.FamilyId, now, cancellationToken);
            return IdentityErrors.InvalidRefreshToken;
        }

        if (current.IsExpired(now))
            return IdentityErrors.InvalidRefreshToken;

        var user = await users.GetByIdAsync(current.UserId, cancellationToken);
        if (user is null)
            return IdentityErrors.InvalidRefreshToken;

        var canAuthenticate = user.CanAuthenticate();
        if (canAuthenticate.IsFailure)
            return canAuthenticate.Error;

        var tokens = await tokenFactory.IssueReplacementAsync(user, current, request.Client, now, cancellationToken);
        if (tokens.IsFailure)
            return tokens.Error;

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // Two refreshes raced on the same token; only one may win. Treat the loser as replay.
            await refreshTokens.RevokeFamilyAsync(current.FamilyId, now, cancellationToken);
            return IdentityErrors.InvalidRefreshToken;
        }

        return tokens.Value;
    }
}
