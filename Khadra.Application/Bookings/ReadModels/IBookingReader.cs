using Khadra.Application.Common;
using Khadra.Domain.Common;

namespace Khadra.Application.Bookings.ReadModels;

/// <summary>One row of a bookings list, from either side of the counter.</summary>
public sealed record BookingListItem(
    Guid BookingId,
    string Reference,
    string Status,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    string PickupMethod,
    decimal TotalPrice,
    string Currency,
    DateTimeOffset CreatedAt,
    // Null when the car has since been removed from the platform: the booking outlives the listing.
    VehicleLabel? Vehicle,
    string DealerName,
    string CustomerName,
    bool HasLiveDispute);

/// <summary>What a booking needs from the other contexts to be readable as a whole.</summary>
/// <remarks>
/// Left-joined by id, never navigated: Fleet, Dealers and Identity are other bounded contexts. Any of
/// the three can legitimately be missing -- a delisted car, a deleted dealer -- and the booking must
/// still render, because it is a financial record that outlives all of them.
/// </remarks>
public sealed record BookingContext(
    VehicleLabel? Vehicle,
    string DealerName,
    string CustomerName,
    Guid? LiveDisputeId);

public sealed record VehicleLabel(
    Guid VehicleId,
    string Make,
    string Model,
    int Year,
    string? Color,
    string PlateNumber,
    // The cover photo's public path, or null: not every seeded car has bytes behind its key.
    string? CoverImageUrl);

public sealed record BookingListFilter(Id? CustomerId, Id? DealerId, string? Status);

public interface IBookingReader
{
    Task<PagedResult<BookingListItem>> ListAsync(
        BookingListFilter filter,
        PageRequest page,
        CancellationToken cancellationToken = default);

    Task<BookingContext> ContextAsync(Id bookingId, CancellationToken cancellationToken = default);
}
