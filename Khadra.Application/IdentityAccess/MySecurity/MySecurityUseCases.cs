using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.IdentityAccess.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using MediatR;

namespace Khadra.Application.IdentityAccess.MySecurity;

// The signed-in person's OWN account security. Not an administrative view of anybody else: reading
// another user's devices and addresses is surveillance, not administration, and nothing in the spec
// asks for it.

/// <param name="AccessTokenMinutes">
/// How long a revoked session can still make requests.
///
/// Revoking a family stops it being refreshed; it does not rotate the security stamp, so an access
/// token already issued stays valid until it expires. The console states this figure rather than
/// promising "signed out immediately", which would be untrue for up to that long.
/// </param>
public sealed record MySessionsView(
    IReadOnlyList<SessionSummary> Sessions,
    int AccessTokenMinutes);

public sealed record GetMySessionsQuery : IQuery<Result<MySessionsView, Error>>;

public sealed record RevokeMySessionCommand(Guid FamilyId) : ICommand<UnitResult<Error>>;

public sealed class GetMySessionsHandler(
    ISessionReader sessions,
    IAccessTokenSettings tokens,
    ICurrentActor actor)
    : IRequestHandler<GetMySessionsQuery, Result<MySessionsView, Error>>
{
    public async Task<Result<MySessionsView, Error>> Handle(GetMySessionsQuery request, CancellationToken cancellationToken)
    {
        if (actor.UserId is not { } userId)
            return IdentityErrors.UserNotFound;

        var list = await sessions.ListForUserAsync(userId, cancellationToken);
        return new MySessionsView(list, tokens.AccessTokenMinutes);
    }
}

public sealed class RevokeMySessionHandler(
    ISessionReader sessions,
    IRefreshTokenRepository refreshTokens,
    IUnitOfWork unitOfWork,
    ICurrentActor actor,
    IClock clock)
    : IRequestHandler<RevokeMySessionCommand, UnitResult<Error>>
{
    public async Task<UnitResult<Error>> Handle(RevokeMySessionCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (actor.UserId is not { } userId)
            return UnitResult.Failure(IdentityErrors.UserNotFound);

        // RevokeFamilyAsync revokes ANY family by id — it is a bulk UPDATE with no owner check, and
        // the logout path gets away with it only because it looks the token up by hash first. Here
        // the id comes from the caller, so ownership is proved before anything is revoked; otherwise
        // one signed-in person could sign out another by guessing a family id.
        if (!await sessions.BelongsToUserAsync(userId, request.FamilyId, cancellationToken))
            return UnitResult.Failure(IdentityErrors.UserNotFound);

        await refreshTokens.RevokeFamilyAsync(request.FamilyId, clock.UtcNow, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return UnitResult.Success<Error>();
    }
}
