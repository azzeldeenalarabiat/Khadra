using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.IdentityAccess.Dtos;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;

namespace Khadra.Application.IdentityAccess;

// Shared by Login, RefreshTokens and ChangePassword: mints the access token and persists the refresh
// token (a new family, or a replacement inside the presented token's family).
public sealed class AuthTokenFactory(
    IAccessTokenIssuer accessTokens,
    IOpaqueTokenService opaqueTokens,
    IRefreshTokenRepository refreshTokens,
    IAuthPolicySettings policy)
{
    public async Task<AuthTokensDto> IssueNewFamilyAsync(
        User user,
        ClientInfo client,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(client);

        var raw = opaqueTokens.Generate();
        var refreshToken = RefreshToken.IssueNewFamily(
            user.Id,
            raw.Hash,
            now,
            policy.RefreshTokenLifetime,
            policy.RefreshFamilyLifetime,
            client.IpAddress,
            client.UserAgent);
        await refreshTokens.AddAsync(refreshToken, cancellationToken);

        return Build(user, raw.Value, refreshToken, now);
    }

    public async Task<Result<AuthTokensDto, Error>> IssueReplacementAsync(
        User user,
        RefreshToken current,
        ClientInfo client,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(client);

        var raw = opaqueTokens.Generate();
        var replacement = current.IssueReplacement(
            raw.Hash,
            now,
            policy.RefreshTokenLifetime,
            client.IpAddress,
            client.UserAgent);

        var rotated = current.Rotate(now, replacement.Id);
        if (rotated.IsFailure)
            return rotated.Error;

        await refreshTokens.AddAsync(replacement, cancellationToken);
        return Build(user, raw.Value, replacement, now);
    }

    private AuthTokensDto Build(User user, string rawRefreshToken, RefreshToken refreshToken, DateTimeOffset now)
    {
        var access = accessTokens.Issue(user, now);
        return new AuthTokensDto(
            access.Token,
            access.ExpiresAt,
            rawRefreshToken,
            refreshToken.ExpiresAt,
            UserDto.From(user));
    }
}
