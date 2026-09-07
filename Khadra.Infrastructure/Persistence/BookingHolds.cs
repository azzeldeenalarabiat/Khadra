using Khadra.Domain.Bookings;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Persistence;

/// <summary>
/// The single definition of "this booking is currently holding a car".
/// </summary>
/// <remarks>
/// There are three places that have to agree about this, and they cannot share code with each other:
/// the overlap guard the booking handler calls, the anti-join the customer catalogue uses to decide
/// what to show as available, and the database's own exclusion constraint. Two of the three can at
/// least share a predicate, and this is it. The third is SQL in a migration, and the domain test
/// pinning the status names is what keeps it in step.
///
/// If they drift, the failure is quiet and awful: search offers a car the guard then refuses, or —
/// worse — search hides a car nothing is actually holding, and a gallery loses bookings it never
/// knew it was turning away.
///
/// Two details that look like fussiness and are not:
///
/// - The statuses are written out. <c>BookingStatus.HoldsVehicle</c> is a computed property and does
///   not translate to SQL, so it has to be spelled here, with the domain remaining the definition.
/// - The status values are captured into locals first. EF cannot translate a member access on a
///   static enumeration inside an expression tree.
/// </remarks>
internal static class BookingHolds
{
    /// <summary>
    /// Narrows to the bookings that hold their vehicle right now.
    /// </summary>
    /// <remarks>
    /// A booking awaiting payment holds the car only until its deadline passes. That term matters
    /// more than it looks: nothing expires those bookings yet (pre-launch checklist item 4), so
    /// without it one abandoned checkout would keep a car off the market permanently.
    ///
    /// The database constraint cannot make the same distinction — an exclusion predicate has no
    /// access to the current time — so it treats a stale unpaid hold as live. The booking handler
    /// therefore has to expire stale holds on the vehicle in the same transaction, before it
    /// inserts, or the guard will say free and the constraint will say taken.
    /// </remarks>
    internal static IQueryable<Booking> Live(IQueryable<Booking> bookings, DateTimeOffset now)
    {
        var pendingPayment = BookingStatus.PendingPayment;
        var requested = BookingStatus.Requested;
        var approved = BookingStatus.Approved;
        var pickedUp = BookingStatus.PickedUp;

        return bookings.Where(booking =>
            (booking.Status == pendingPayment && booking.PaymentDeadline > now) ||
            booking.Status == requested ||
            booking.Status == approved ||
            booking.Status == pickedUp);
    }

    /// <summary>
    /// The live holds that collide with a candidate rental claiming the car from
    /// <paramref name="candidateHoldStart"/> until <paramref name="candidateEnd"/>.
    /// </summary>
    /// <remarks>
    /// Composed as a query rather than written as a predicate over a loaded Booking, because both
    /// callers need this to reach the database: one as an EXISTS, the other as an anti-join inside a
    /// paged catalogue search. A bool-returning helper could not be translated at all.
    ///
    /// Half-open on both sides, and the lower bound is the HOLD start, not the period start, so the
    /// turnaround gap counts. The candidate is padded by its caller using the CURRENT buffer; each
    /// stored booking carries the buffer frozen onto it when it was made. That asymmetry is
    /// deliberate and is how every other frozen term behaves — an existing booking is judged by the
    /// rule it was made under, a new one by today's.
    /// </remarks>
    internal static IQueryable<Booking> Colliding(
        IQueryable<Booking> bookings,
        DateTimeOffset now,
        DateTimeOffset candidateHoldStart,
        DateTimeOffset candidateEnd) =>
        Live(bookings, now)
            .Where(booking => booking.HoldStart < candidateEnd && booking.Period.End > candidateHoldStart);
}
