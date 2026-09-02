using Khadra.Domain.IdentityAccess;

namespace Khadra.Application.Common.Ports;

public sealed record IssuedAccessToken(string Token, DateTimeOffset ExpiresAt);

public interface IAccessTokenIssuer
{
    IssuedAccessToken Issue(User user, DateTimeOffset now);
}
