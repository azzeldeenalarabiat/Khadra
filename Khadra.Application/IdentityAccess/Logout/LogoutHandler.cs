using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess.Repositories;
using MediatR;

namespace Khadra.Application.IdentityAccess.Logout;

public sealed class LogoutHandler(
    IRefreshTokenRepository refreshTokens,
    IOpaqueTokenService opaqueTokens,
    IClock clock)
    : IRequestHandler<LogoutCommand, UnitResult<Error>>
{
    public async Task<UnitResult<Error>> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = clock.UtcNow;
        if (request.AllDevices)
        {
            await refreshTokens.RevokeAllForUserAsync(request.UserId, now, cancellationToken);
            return UnitResult.Success<Error>();
        }

        var token = await refreshTokens.GetByHashAsync(opaqueTokens.Hash(request.RefreshToken), cancellationToken);
        if (token is not null && token.UserId == request.UserId)
            await refreshTokens.RevokeFamilyAsync(token.FamilyId, now, cancellationToken);

        return UnitResult.Success<Error>();
    }
}
