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
    string CorrelationId { get; }
}
