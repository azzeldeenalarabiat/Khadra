using System.Linq.Expressions;
using CSharpFunctionalExtensions;
using Khadra.Domain.Common;

namespace Khadra.Domain.Bookings;

/// <summary>
/// One definition of "a window closed on this booking and the row has not caught up".
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Booking.HasLapsed"/> has always been the truth, and it reads the CLOCK rather than the
/// status: a request past its decision deadline and an approval past its payment deadline are over
/// the instant the deadline passes, whether or not anything has run since. That is what keeps the
/// car free and the Pay button hidden without a background job being alive.
/// </para>
/// <para>
/// What was missing is everything else. The predicate lived on the aggregate, so a SQL reader could
/// not ask it, and three callers out of everything that touches a booking remembered to. On
/// Render's free tier the API sleeps after fifteen minutes, so the settlement sweep may not have run
/// for hours — and the dealer's queue counted every expired request as pending, because it counted
/// <c>Status == Requested</c> and nothing else.
/// </para>
/// <para>
/// So this type states the rule twice, deliberately, and <c>BookingLapseTests</c> holds the two
/// halves to each other over a matrix of statuses and clocks:
/// </para>
/// <list type="bullet">
/// <item><see cref="HasLapsedAt"/> — an expression a reader can put in a WHERE clause;</item>
/// <item><see cref="Settle"/> — the transition an action takes before it acts.</item>
/// </list>
/// <para>
/// The alternative was a reader spelling the dates out itself, which is the second definition this
/// project has been bitten by before: <c>CatalogueReader</c> carries the same warning about
/// <c>IsBookable</c>, and for the same reason — when the two drift, the failure is silent and points
/// the wrong way.
/// </para>
/// </remarks>
public static class BookingLapse
{
    /// <summary>
    /// The lapse rule as a predicate a database can evaluate.
    /// </summary>
    /// <remarks>
    /// Mirrors <see cref="Booking.HasLapsed"/> exactly. Written with explicit member access rather
    /// than by calling the method, because EF cannot translate a method on the entity — which is the
    /// whole reason this duplication exists and the reason it is tested rather than trusted.
    /// </remarks>
    public static Expression<Func<Booking, bool>> HasLapsedAt(DateTimeOffset now) =>
        booking =>
            (booking.Status == BookingStatus.Requested && now >= booking.DecisionDeadline)
            || (booking.Status == BookingStatus.Approved
                && booking.PaymentDeadline != null
                && now >= booking.PaymentDeadline);

    /// <summary>The negation, for the far more common "only the ones still live" reading.</summary>
    public static Expression<Func<Booking, bool>> HasNotLapsedAt(DateTimeOffset now) =>
        booking =>
            !((booking.Status == BookingStatus.Requested && now >= booking.DecisionDeadline)
                || (booking.Status == BookingStatus.Approved
                    && booking.PaymentDeadline != null
                    && now >= booking.PaymentDeadline));

    /// <summary>
    /// Settles a lapse if there is one, and says whether anything changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Idempotent by construction: it asks the aggregate's own predicate first, and the aggregate's
    /// expiry methods refuse a booking in the wrong status anyway. Calling it on a booking that has
    /// not lapsed, or on one already settled, does nothing and reports <c>false</c>.
    /// </para>
    /// <para>
    /// It does NOT save. The caller's unit of work decides that, which is what keeps a read from
    /// writing: a query that loads a lapsed booking gets an aggregate telling the truth, and a
    /// command that loads the same booking persists the settlement along with whatever it came to
    /// do. Under concurrency both writers make the SAME transition, and the loser of the xmin race
    /// retries into a context that reads it already settled.
    /// </para>
    /// </remarks>
    public static bool Settle(Booking booking, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(booking);
        if (!booking.HasLapsed(now)) return false;

        var settled = booking.Status == BookingStatus.Requested
            ? booking.ExpireUnanswered(now)
            : booking.ExpireUnpaid(now);

        // A refusal here would mean the predicate and the expiry methods disagree, which the tests
        // forbid. Swallowed rather than thrown because this runs on the read path: a booking nobody
        // can settle must not take down the screen that merely wanted to show it.
        return settled.IsSuccess;
    }
}
