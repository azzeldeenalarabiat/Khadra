using Khadra.Domain.Common;

namespace Khadra.Domain.Dealers.Events;

public sealed record DealerRegistrationSubmitted(Id DealerId, Id OwnerUserId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record DealerResubmitted(Id DealerId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record DealerApproved(Id DealerId, Id AdminUserId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record DealerRejected(Id DealerId, Id AdminUserId, string Reason, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record DealerClarificationRequested(Id DealerId, Id AdminUserId, string Note, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record DealerSuspended(Id DealerId, Id AdminUserId, string Reason, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record DealerEmployeeHired(Id DealerId, Id EmployeeId, Id UserId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record DealerEmployeeDeactivated(Id DealerId, Id EmployeeId, Id UserId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record DealerDeliveryChanged(Id DealerId, bool IsEnabled, decimal RadiusKm, DateTimeOffset OccurredAt) : IDomainEvent;
