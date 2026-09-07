using Khadra.Domain.Common;

namespace Khadra.Domain.Bookings.Events;

public sealed record BookingCreated(
    Id BookingId,
    Id CustomerId,
    Id DealerId,
    Id VehicleId,
    DateTimeOffset OccurredAt) : IDomainEvent;

// A customer has asked for a car. Nothing has been paid -- since 2026-09-07 the deposit comes after
// the dealer answers -- so this is the whole of it: the request is now the dealer's to answer.
public sealed record BookingRequested(Id BookingId, Id DealerId, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>The deposit has been paid and the rental is on.</summary>
/// <remarks>
/// Separate from <see cref="BookingRequested"/> because they are now different moments. Under the
/// old order a request WAS a payment; since 2026-09-07 a request costs nothing and this is the
/// point at which money exists, which is what Payments and the dealer both care about.
/// </remarks>
public sealed record BookingConfirmed(
    Id BookingId,
    Id DealerId,
    Id VehicleId,
    DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record BookingApproved(
    Id BookingId,
    Id DealerId,
    Id ActedByUserId,
    DateTimeOffset OccurredAt,
    // The dealer's word to the customer, if any, so a notification can carry it.
    string? Note = null) : IDomainEvent;

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
