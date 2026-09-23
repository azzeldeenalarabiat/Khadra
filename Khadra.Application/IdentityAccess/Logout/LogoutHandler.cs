using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess.Repositories;
using MediatR;

namespace Khadra.Application.IdentityAccess.Logout;

public sealed class LogoutHandler(
    IRefreshTokenRepository refreshTokens,
    IPushDeviceRepository pushDevices,
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
        {
            await refreshTokens.RevokeFamilyAsync(token.FamilyId, now, cancellationToken);
            // The phone that signed out stops being woken for this account. Delivery also checks the
            // session is live, so this is tidiness on top of a guard rather than the guard itself.
            await pushDevices.RevokeForSessionAsync(request.UserId, token.FamilyId, now, cancellationToken);
        }

        return UnitResult.Success<Error>();
    }
}
