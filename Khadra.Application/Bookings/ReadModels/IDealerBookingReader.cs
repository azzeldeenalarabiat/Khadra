using Khadra.Application.Common;
using Khadra.Domain.Common;

namespace Khadra.Application.Bookings.ReadModels;

// Raw booking FACTS for one dealership's console. Instants, amounts and ids only; the handler does the
// calendar bucketing (IReportingCalendar) and the money arithmetic (Money/Percentage), because the
// database knows neither the Amman calendar nor how a Percentage rounds.

/// <summary>Headcounts the dashboard leads with.</summary>
/// <param name="AwaitingDeposit">
/// Approved and not paid for. These hold a car on nothing but a clock, and the dealer can neither
/// prepare them nor count on them.
/// </param>
/// <param name="Confirmed">
/// Approved AND paid for: the rentals that are actually going ahead, and the cars to have ready.
/// </param>
public sealed record DealerBookingCounts(
    int Requested,
    DateTimeOffset? OldestRequestedAt,
    int AwaitingDeposit,
    int Confirmed,
    int PickedUp,
    int OverdueReturns);

/// <summary>A pickup or a return the dealer has coming.</summary>
/// <param name="CustomerName">
/// Null when the customer's account no longer resolves. A fact rather than a sentence: the console
/// words that case in the reader's own language, which an English stand-in written here never was.
/// </param>
public sealed record UpcomingHandover(
    Guid BookingId,
    string Reference,
    string Status,
    DateTimeOffset When,
    string PickupMethod,
    Guid VehicleId,
    string? CustomerName,
    bool IsOverdue);

/// <summary>One booking's contribution to revenue, at the rate FROZEN on that booking.</summary>
public sealed record RevenueFact(
    Guid BookingId,
    string Status,
    DateTimeOffset EarnedAt,
    decimal RentalTotal,
    string Currency,
    decimal CommissionPercent);

/// <summary>The stretch of a rental that overlaps a reporting window, for occupancy.</summary>
public sealed record OccupancyFact(Guid VehicleId, DateTimeOffset Start, DateTimeOffset End, string Status);

/// <summary>Something a member of staff did to a booking, from the booking's own history.</summary>
/// <param name="ActorUserId">Null when no person signed the change: the rental office acted as itself.</param>
/// <param name="ActorName">
/// Null in two cases, told apart by <paramref name="ActorUserId"/>: no id at all (the rental office
/// acted), or an id whose account no longer resolves (a former member of staff). The console words both.
/// </param>
public sealed record DealerActivityEntry(
    Guid BookingId,
    string Reference,
    string ToStatus,
    string? FromStatus,
    Guid? ActorUserId,
    string? ActorName,
    string? Reason,
    DateTimeOffset OccurredAt);

/// <summary>One booking status and how many of this dealer's bookings are in it.</summary>
public sealed record DealerStatusCount(string Status, int Count);

/// <summary>
/// The cheapest honest answer to "has anything in this dealer's queue changed?".
/// </summary>
/// <remarks>
/// Deliberately NOT a set of figures for a screen to render. It exists to be compared with the last
/// one, and nothing else: the console reloads its real readers when it differs and does nothing when
/// it does not. Two numbers that mean the same thing, arriving by two routes, is how a queue and its
/// badge start disagreeing — so the pulse never becomes a second answer to a question
/// <see cref="CountsAsync"/> already answers.
/// </remarks>
/// <param name="ByStatus">
/// Moves when a booking is created, approved, rejected, paid for, cancelled, picked up or returned.
/// </param>
/// <param name="Live">
/// How many have not lapsed. This is what makes the signature move when NOTHING was written: a
/// request past its decision deadline is over the instant the clock says so, while its row still
/// reads Requested until the settlement sweep catches up.
/// </param>
/// <param name="Disputed">
/// How many carry a live dispute — the one thing a row shows that moves without its STATUS moving.
/// </param>
/// <remarks>
/// What this watches is what a dealer's queue can be wrong about, and the list is deliberately
/// written down rather than assumed: booking status, lapse, and the dispute flag. Anything that
/// starts changing a row without changing one of those three has to be added here, or the console
/// will be stale and certain it is not — which is the only failure mode of a pulse that matters.
/// </remarks>
public sealed record DealerQueueSignature(
    IReadOnlyList<DealerStatusCount> ByStatus,
    int Live,
    int Disputed);

public interface IDealerBookingReader
{
    Task<DealerBookingCounts> CountsAsync(Id dealerId, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// A change marker for this dealer's bookings, cheap enough to ask for every thirty seconds.
    /// </summary>
    /// <remarks>
    /// Two indexed round trips, against the eight that <see cref="CountsAsync"/> costs and the
    /// composite the dashboard costs. That difference is the whole reason this exists: the console
    /// needs to know WHETHER to re-read far more often than it needs to re-read.
    /// </remarks>
    Task<DealerQueueSignature> QueueSignatureAsync(Id dealerId, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bookings the dealer has answered that start inside [from, to) -- Approved AND Confirmed.
    /// </summary>
    /// <remarks>
    /// The unpaid ones are included deliberately. A gallery preparing its week needs to know that a
    /// car is spoken for on Tuesday and that the deposit has not landed, and each row carries its own
    /// status so the screen can say which is which.
    /// </remarks>
    Task<IReadOnlyList<UpcomingHandover>> UpcomingPickupsAsync(Id dealerId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>Cars out on rental due back inside [from, to), plus any already overdue.</summary>
    Task<IReadOnlyList<UpcomingHandover>> UpcomingReturnsAsync(Id dealerId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>Vehicles this dealer's live bookings hold right now.</summary>
    Task<IReadOnlyList<Guid>> HeldVehicleIdsAsync(Id dealerId, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Returned and Completed bookings whose car came back inside [from, to); PickedUp ones are "in progress".</summary>
    Task<IReadOnlyList<RevenueFact>> RevenueAsync(Id dealerId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>Rentals (PickedUp, Returned, Completed) whose period overlaps [from, to).</summary>
    Task<IReadOnlyList<OccupancyFact>> OccupancyAsync(Id dealerId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>The dealership's trail, or one person's within it when <paramref name="actorUserId"/> is given.</summary>
    Task<PagedResult<DealerActivityEntry>> ActivityAsync(Id dealerId, PageRequest page, Id? actorUserId = null, CancellationToken cancellationToken = default);
}
