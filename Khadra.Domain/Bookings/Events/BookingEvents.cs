using Khadra.Domain.Common;

namespace Khadra.Domain.Bookings.Events;

public sealed record BookingCreated(
    Id BookingId,
    Id CustomerId,
    Id DealerId,
    Id VehicleId,
    DateTimeOffset OccurredAt) : IDomainEvent;

// The deposit cleared. Payments listens to nothing here; the booking is simply now visible to the dealer.
public sealed record BookingRequested(Id BookingId, Id DealerId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record BookingApproved(
    Id BookingId,
    Id DealerId,
    Id ActedByUserId,
    DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record BookingRejected(
    Id BookingId,
    Id DealerId,
    Id ActedByUserId,
    string Reason,
    DateTimeOffset OccurredAt) : IDomainEvent;

// Nobody acted in time. Always refundable in full: no party is at fault.
public sealed record BookingExpired(Id BookingId, Id VehicleId, string Reason, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record BookingCancelled(
    Id BookingId,
    Id VehicleId,
    string CancelledByParty,
    string AttributedToParty,
    decimal MaxPenaltyAmount,
    string CurrencyCode,
    bool RequiresTicketToEnforce,
    DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record BookingMarkedNoShow(
    Id BookingId,
    Id VehicleId,
    string AttributedToParty,
    DateTimeOffset OccurredAt) : IDomainEvent;

// Commission crystallises here: before pickup the booking can still evaporate with a full refund.
public sealed record BookingPickedUp(
    Id BookingId,
    Id DealerId,
    Id VehicleId,
    DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record BookingReturned(Id BookingId, Id VehicleId, DateTimeOffset OccurredAt) : IDomainEvent;

// Settled with no open dispute. Reviews unlock on this event.
public sealed record BookingCompleted(
    Id BookingId,
    Id CustomerId,
    Id DealerId,
    DateTimeOffset OccurredAt) : IDomainEvent;
