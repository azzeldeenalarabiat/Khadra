using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;

namespace Khadra.Application.Common;

// Trusted, server-validated identity of the caller (from JWT claims). Never populated from request bodies.
public interface ICurrentActor
{
    bool IsAuthenticated { get; }
    Id? UserId { get; }
    UserRole? Role { get; }

    // The display name from the token. Needed because an audit entry snapshots WHO acted rather than
    // pointing at them, and reading it from the already-validated token avoids a database round trip
    // on every privileged action.
    string? Name { get; }
    Guid? SecurityStamp { get; }

    /// <summary>
    /// Which of this user's sessions is making the request: the refresh-token family the access
    /// token was minted for.
    /// </summary>
    /// <remarks>
    /// Null for a token issued before the claim existed, which is a state that lasts one access
    /// token. Nothing may refuse a request over it — the only thing that reads it is the devices
    /// screen, marking the row you are on.
    /// </remarks>
    Guid? SessionId { get; }

    string CorrelationId { get; }
}
