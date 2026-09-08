using Khadra.Domain.Common;

namespace Khadra.Domain.Payments.Events;

/// <summary>The provider captured money and the booking took it.</summary>
public sealed record PaymentApplied(
    Id PaymentId,
    Id BookingId,
    Id CustomerId,
    decimal Amount,
    string CurrencyCode,
    DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// The provider captured money and no booking could take it. A refund is already recorded against
/// the payment; this exists so anything watching can say so out loud.
/// </summary>
public sealed record PaymentOrphaned(
    Id PaymentId,
    Id BookingId,
    Id CustomerId,
    decimal Amount,
    string CurrencyCode,
    string Reason,
    DateTimeOffset OccurredAt) : IDomainEvent;
