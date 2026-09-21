using Khadra.Domain.IdentityAccess;

namespace Khadra.Application.Common.Ports;

public sealed record IssuedAccessToken(string Token, DateTimeOffset ExpiresAt);

public interface IAccessTokenIssuer
{
    /// <param name="sessionId">
    /// The refresh-token FAMILY this access token belongs to, carried as a claim so the server can
    /// tell which of somebody's sessions is the one asking.
    /// </param>
    /// <remarks>
    /// A family is a session: it starts at one sign-in and survives every rotation, so the value is
    /// stable for as long as the device stays signed in. It is not a secret — the same id is listed
    /// back to its owner on their own devices screen — and it identifies nothing outside this
    /// account.
    /// </remarks>
    IssuedAccessToken Issue(User user, Guid sessionId, DateTimeOffset now);
}
