using Khadra.Domain.Common;

namespace Khadra.Domain.Fleet.Events;

public sealed record VehicleAdded(Id VehicleId, Id DealerId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record VehiclePublished(Id VehicleId, Id DealerId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record VehicleHidden(Id VehicleId, Id DealerId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record VehicleRateChanged(
    Id VehicleId,
    decimal PreviousDailyRate,
    decimal NewDailyRate,
    string CurrencyCode,
    DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record VehicleDeleted(Id VehicleId, Id DealerId, DateTimeOffset OccurredAt) : IDomainEvent;
