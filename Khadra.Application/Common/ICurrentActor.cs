using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;

namespace Khadra.Application.Common;

// Trusted, server-validated identity of the caller (from JWT claims). Never populated from request bodies.
public interface ICurrentActor
{
    bool IsAuthenticated { get; }
    Id? UserId { get; }
    UserRole? Role { get; }
    Guid? SecurityStamp { get; }
    string CorrelationId { get; }
}
