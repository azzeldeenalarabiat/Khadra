using Khadra.Domain.Common;

namespace Khadra.Domain.IdentityAccess.Events;

public sealed record UserRegistered(Id UserId, string Email, string Role, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record UserEmailVerified(Id UserId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record UserPasswordChanged(Id UserId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record UserSuspended(Id UserId, string Reason, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record UserReactivated(Id UserId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record UserDeleted(Id UserId, DateTimeOffset OccurredAt) : IDomainEvent;
