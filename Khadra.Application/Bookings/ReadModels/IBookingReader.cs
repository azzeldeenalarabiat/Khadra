using Khadra.Application.Common;
using Khadra.Domain.Bookings;
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

/// <summary>
/// Which bookings. Either a raw domain status or a TAB, the console's vocabulary, resolved here so
/// the client never encodes state names and the tab counts are the database's answer.
/// </summary>
public sealed record BookingListFilter(Id? CustomerId, Id? DealerId, string? Status, string? Tab = null, Guid? VehicleId = null);

/// <summary>
/// The dealer's tabs mapped onto the domain (design: Dealer Console, TABS). The design's "Confirmed"
/// has no domain state and is dropped; "Upcoming" IS Approved (every approved booking is still ahead
/// of its pickup); "Disputed" is orthogonal to status -- a booking is Returned AND disputed.
/// PendingPayment never reaches a dealer's list: no deposit has cleared, so nothing has been asked
/// of them yet (spec 5.3).
/// </summary>
public static class BookingTabs
{
    public const string All = "all";
    public const string Pending = "pending";
    public const string Upcoming = "upcoming";
    public const string Active = "active";
    public const string Returned = "returned";
    public const string Completed = "completed";
    public const string Closed = "closed";
    public const string Disputed = "disputed";

    public static readonly IReadOnlyList<string> Names =
        [All, Pending, Upcoming, Active, Returned, Completed, Closed, Disputed];

    public static IReadOnlyList<BookingStatus>? StatusesFor(string tab) =>
        tab.ToLowerInvariant() switch
        {
            Pending => [BookingStatus.Requested],
            Upcoming => [BookingStatus.Approved],
            Active => [BookingStatus.PickedUp],
            Returned => [BookingStatus.Returned],
            Completed => [BookingStatus.Completed],
            Closed => [BookingStatus.Cancelled, BookingStatus.Rejected, BookingStatus.NoShow, BookingStatus.Expired],
            // "all" and "disputed" are not status lists.
            _ => null,
        };

    public static bool IsKnown(string? tab) =>
        tab is null || Names.Contains(tab.ToLowerInvariant());
}

public interface IBookingReader
{
    Task<PagedResult<BookingListItem>> ListAsync(
        BookingListFilter filter,
        PageRequest page,
        CancellationToken cancellationToken = default);

    /// <summary>How many bookings sit behind each tab, for the same scope (customer or dealer).</summary>
    Task<IReadOnlyDictionary<string, int>> TabCountsAsync(
        BookingListFilter scope,
        CancellationToken cancellationToken = default);

    Task<BookingContext> ContextAsync(Id bookingId, CancellationToken cancellationToken = default);
}
