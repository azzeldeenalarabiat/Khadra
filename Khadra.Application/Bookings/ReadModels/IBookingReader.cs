using Khadra.Application.Common;
using Khadra.Application.Payments.Dtos;
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
    // Kept as a string (an English stand-in when the dealership no longer resolves) only for older customer apps.
    string DealerName,
    // True exactly when the dealership no longer resolves, so DealerName holds the stand-in.
    bool DealerRemoved,
    // Kept as a string (an English stand-in when the account no longer resolves) only for older clients.
    string CustomerName,
    // True exactly when the customer's account no longer resolves, so CustomerName holds the stand-in.
    bool CustomerAccountClosed,
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
    // Kept as a string (an English stand-in when the dealership no longer resolves) only for older customer apps.
    string DealerName,
    // True exactly when the dealership no longer resolves, so DealerName holds the stand-in.
    bool DealerRemoved,
    /// <summary>The city the dealership operates from, by LOOKUP ID. Null when it has none.</summary>
    /// <remarks>
    /// An id, not a name, because that is the shape every read model on this platform already uses
    /// for a city — the catalogue and the public gallery page both send one — and each client
    /// resolves it against <c>/api/v1/cities</c> in its own reader's language. A name composed here
    /// would be the first server-side join into PlatformSettings and a second way of naming one
    /// thing.
    ///
    /// LIVE, like <c>DealerName</c>: an office that moves re-labels its old bookings. That is
    /// correct — a city is not a term the booking froze, and nothing is priced or judged from it.
    /// </remarks>
    Guid? DealerCityId,
    // Kept as a string (an English stand-in when the account no longer resolves) only for older clients.
    string CustomerName,
    // True exactly when the customer's account no longer resolves, so CustomerName holds the stand-in.
    bool CustomerAccountClosed,
    Guid? LiveDisputeId,
    /// <summary>
    /// The customer's own review of this booking, if they have left one.
    /// </summary>
    /// <remarks>
    /// Correlated here rather than fetched by the app, so a booking screen can decide between
    /// "rate this rental" and "you rated it" without a second request that would answer 404 for the
    /// ordinary case of not having reviewed yet.
    /// </remarks>
    Guid? MyReviewId,
    /// <summary>
    /// Whether the deposit can be paid right now, for the screens that offer it.
    /// </summary>
    /// <remarks>
    /// Defaulted to null, and null means "nobody asked" rather than "no". Only the customer's own
    /// booking screens compose it: an admin looking at a rental has no Pay button, and computing the
    /// verdict for them would be a query per booking for an answer nothing renders.
    /// </remarks>
    PaymentAvailabilityDto? Payment = null);

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

/// <summary>
/// The one booking a customer most needs to see when they open the app.
/// </summary>
/// <remarks>
/// <para>
/// It exists as a QUERY because "next" is a business judgement, not a sort. A booking whose deposit
/// is due in three hours outranks a rental starting tomorrow, which outranks a request nobody has
/// answered — and the app deciding that for itself would be a screen ranking bookings by a rule the
/// server owns, drifting the moment a state is added.
/// </para>
/// <para>
/// It matters more than a convenience. There is no push channel (pre-launch checklist item 73), so a
/// customer learns their booking was approved by OPENING THE APP — and since 2026-09-11 they have
/// two hours from the approval to pay, not a day. This is the only surface that can tell them in
/// time, and until it existed approvals expired unread, with the gallery's decision wasted and a car
/// held for nothing. Shortening the window made that worse, not better, which is why item 73 is now
/// a dependency of the window rather than a nicety beside it (pre-launch item 90).
/// </para>
/// </remarks>
/// <param name="Reason">
/// WHY this one was chosen, as a stable code the app maps to a sentence. Not an English phrase: the
/// wording is the client's, in the reader's own language.
/// </param>
public sealed record NextBooking(BookingListItem Booking, string Reason);

/// <summary>Why one booking outranked the others. Ordered most urgent first.</summary>
public static class NextBookingReason
{
    /// <summary>Approved, and the deposit is owed before a deadline that will not wait.</summary>
    public const string AwaitingPayment = "AwaitingPayment";

    /// <summary>The car is out. Only the return date is ahead.</summary>
    public const string InProgress = "InProgress";

    /// <summary>Paid for and not yet collected.</summary>
    public const string Upcoming = "Upcoming";

    /// <summary>Asked for, and the gallery has not answered.</summary>
    public const string AwaitingDecision = "AwaitingDecision";
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

    /// <summary>
    /// The customer's most pressing live booking, or null when they have none.
    /// </summary>
    /// <remarks>
    /// Null is the ordinary answer for most people most of the time, and the landing surface renders
    /// nothing at all for it — never a placeholder card.
    /// </remarks>
    /// <param name="now">
    /// The instant "live" is judged against. A booking whose decision or payment window has closed is
    /// not the customer's next one, whatever its stored status still reads — see <c>BookingLapse</c>.
    /// </param>
    Task<NextBooking?> NextForCustomerAsync(
        Id customerId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
