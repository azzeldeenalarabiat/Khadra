using Khadra.Application.Common;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Infrastructure.Security;

namespace Khadra.WebAPI.Security;

// Reads only server-validated claims from the bearer token. Never trusts request bodies or headers.
internal sealed class HttpCurrentActor(IHttpContextAccessor httpContextAccessor) : ICurrentActor
{
    private HttpContext? Context => httpContextAccessor.HttpContext;

    public bool IsAuthenticated => Context?.User.Identity?.IsAuthenticated == true;

    public Id? UserId =>
        Guid.TryParse(Context?.User.FindFirst(KhadraClaimTypes.Subject)?.Value, out var id) ? Id.From(id) : null;

    public UserRole? Role
    {
        get
        {
            var name = Context?.User.FindFirst(KhadraClaimTypes.Role)?.Value;
            return name is null ? null : Enumeration.GetAll<UserRole>().SingleOrDefault(role => role.Name == name);
        }
    }

    public string? Name => Context?.User.FindFirst(KhadraClaimTypes.Name)?.Value;

    public Guid? SecurityStamp =>
        Guid.TryParse(Context?.User.FindFirst(KhadraClaimTypes.SecurityStamp)?.Value, out var stamp) ? stamp : null;

    public string CorrelationId => Context?.TraceIdentifier ?? "n/a";
}
