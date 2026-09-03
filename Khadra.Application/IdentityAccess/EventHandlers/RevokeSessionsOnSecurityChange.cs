using Khadra.Application.Common;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess.Events;
using Khadra.Domain.IdentityAccess.Repositories;

namespace Khadra.Application.IdentityAccess.EventHandlers;

// Any security-relevant change to the account ends every refresh-token family. Access tokens die on
// their next request because the security stamp rotated at the same time.
public sealed class RevokeSessionsOnPasswordChanged(IRefreshTokenRepository refreshTokens, IClock clock)
    : IDomainEventHandler<UserPasswordChanged>
{
    public Task HandleAsync(UserPasswordChanged domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return refreshTokens.RevokeAllForUserAsync(domainEvent.UserId, clock.UtcNow, cancellationToken);
    }
}

public sealed class RevokeSessionsOnSuspended(IRefreshTokenRepository refreshTokens, IClock clock)
    : IDomainEventHandler<UserSuspended>
{
    public Task HandleAsync(UserSuspended domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return refreshTokens.RevokeAllForUserAsync(domainEvent.UserId, clock.UtcNow, cancellationToken);
    }
}

public sealed class RevokeSessionsOnRevoked(IRefreshTokenRepository refreshTokens, IClock clock)
    : IDomainEventHandler<UserSessionsRevoked>
{
    public Task HandleAsync(UserSessionsRevoked domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return refreshTokens.RevokeAllForUserAsync(domainEvent.UserId, clock.UtcNow, cancellationToken);
    }
}

public sealed class RevokeSessionsOnDeleted(IRefreshTokenRepository refreshTokens, IClock clock)
    : IDomainEventHandler<UserDeleted>
{
    public Task HandleAsync(UserDeleted domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return refreshTokens.RevokeAllForUserAsync(domainEvent.UserId, clock.UtcNow, cancellationToken);
    }
}
