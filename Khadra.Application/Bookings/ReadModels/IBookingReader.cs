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
    // The billed calendar days, frozen on the booking. A screen must never recompute this from the
    // two instants above: subtracting them answers "how long was it out", which is the rule the
    // platform stopped billing by on 2026-09-07, and it would contradict the invoice.
    int Days,
    string PickupMethod,
    decimal TotalPrice,
    string Currency,
    DateTimeOffset CreatedAt,
    // Null when the car has since been removed from the platform: the booking outlives the listing.
    VehicleLabel? Vehicle,
    string DealerName,
    string CustomerName,
    bool HasLiveDispute,
    // Both parties by id, so a platform-wide row can open the dealership or the customer behind it.
    // A dealer or customer reading their own list already knows one of them; the Admin knows neither.
    Guid DealerId,
    Guid CustomerId);

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
/// <param name="Reference">One booking by its reference, matched exactly. The Admin's search box.</param>
public sealed record BookingListFilter(
    Id? CustomerId,
    Id? DealerId,
    string? Status,
    string? Tab = null,
    Guid? VehicleId = null,
    string? Reference = null);

/// <summary>
/// The dealer's tabs mapped onto the domain (design: Dealer Console, TABS).
///
/// "Upcoming" is Approved AND Confirmed: both are answered and neither has been collected, and what
/// separates them -- whether the deposit has cleared -- is a fact about one booking, shown on its
/// row, not a queue of its own. The design drew a "Confirmed" tab back when nothing in the domain
/// could be confirmed; it is served by the status on the row instead of by a tab that would split
/// one dealer's week in half.
///
/// "Disputed" is orthogonal to status, since a booking is Returned AND disputed.
///
/// Every request reaches the dealer's list. It used to be that an unpaid one did not, because until
/// 2026-09-07 a request without a deposit had asked the dealer for nothing; now it is precisely the
/// thing they must answer.
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
            Upcoming => [BookingStatus.Approved, BookingStatus.Confirmed],
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
