using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.Bookings;

/// <summary>
/// The two statements of the lapse rule, held to each other.
/// </summary>
/// <remarks>
/// <para>
/// <c>BookingLapse.HasLapsedAt</c> exists because EF cannot translate a method on an entity, so the
/// rule that lives in <c>Booking.HasLapsed</c> has to be written a second time as an expression. A
/// second definition of a rule is how this project has been hurt before — <c>CatalogueReader</c>
/// carries the same warning about <c>IsBookable</c> — and the mitigation there is the one used here:
/// do not assert a hand-written list of expectations, ask the DOMAIN what each case should be and
/// hold the expression to that answer.
/// </para>
/// <para>
/// If the two ever drift, the failure is silent and points the wrong way: a dealer's queue counting
/// expired requests as pending, or a live request vanishing from it.
/// </para>
/// </remarks>
public sealed class BookingLapseTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    /// <summary>
    /// Every status, on both sides of both deadlines, compiled and compared.
    /// </summary>
    [Fact]
    public void The_expression_agrees_with_the_aggregate_on_every_case()
    {
        var predicate = BookingLapse.HasLapsedAt(Now).Compile();
        var negated = BookingLapse.HasNotLapsedAt(Now).Compile();

        foreach (var (booking, label) in Cases())
        {
            Assert.True(
                booking.HasLapsed(Now) == predicate(booking),
                $"The expression and the aggregate disagree about: {label}.");

            // The negation is written out rather than derived, so it is checked rather than assumed.
            Assert.True(
                negated(booking) == !predicate(booking),
                $"The negated expression is not the negation for: {label}.");
        }
    }

    /// <summary>A booking that has not lapsed is left exactly as it was.</summary>
    [Fact]
    public void Settling_a_live_booking_changes_nothing()
    {
        var booking = Build.RequestedBooking();
        var before = booking.Status;

        Assert.False(BookingLapse.Settle(booking, Now));
        Assert.Same(before, booking.Status);
    }

    /// <summary>
    /// A request nobody answered settles to Expired, and settling again is a no-op.
    /// </summary>
    /// <remarks>
    /// Idempotence is the property that makes the seam safe to call from anywhere — including twice
    /// in one request, and from two concurrent requests that both loaded the booking live.
    /// </remarks>
    [Fact]
    public void An_unanswered_request_settles_once_and_stays_settled()
    {
        var booking = Build.RequestedBooking();
        var after = booking.DecisionDeadline.AddMinutes(1);

        Assert.True(BookingLapse.Settle(booking, after));
        Assert.Same(BookingStatus.Expired, booking.Status);

        // Second call: nothing left to do, and it says so rather than throwing or transitioning again.
        Assert.False(BookingLapse.Settle(booking, after));
        Assert.Same(BookingStatus.Expired, booking.Status);
    }

    /// <summary>An approval nobody paid settles the same way, through the other door.</summary>
    [Fact]
    public void An_unpaid_approval_settles_once_and_stays_settled()
    {
        var booking = Build.ApprovedBooking();
        var after = booking.PaymentDeadline!.Value.AddMinutes(1);

        Assert.True(BookingLapse.Settle(booking, after));
        Assert.Same(BookingStatus.Expired, booking.Status);
        Assert.False(BookingLapse.Settle(booking, after));
    }

    /// <summary>
    /// The exact boundary. A deadline is the first instant the window is CLOSED, not the last it is
    /// open, and the aggregate's own guards use `>=` — so the expression must too.
    /// </summary>
    [Fact]
    public void The_deadline_instant_itself_has_already_lapsed()
    {
        var predicate = BookingLapse.HasLapsedAt(Now).Compile();

        var request = Build.RequestedBooking();
        var atDeadline = BookingLapse.HasLapsedAt(request.DecisionDeadline).Compile();
        Assert.True(atDeadline(request));
        Assert.True(request.HasLapsed(request.DecisionDeadline));

        var oneTickEarlier = BookingLapse
            .HasLapsedAt(request.DecisionDeadline.AddTicks(-1))
            .Compile();
        Assert.False(oneTickEarlier(request));
        Assert.False(request.HasLapsed(request.DecisionDeadline.AddTicks(-1)));

        // And `Now` is well inside the window, so the fixture itself is not the reason it passes.
        Assert.False(predicate(request));
    }

    /// <summary>Every case the matrix walks, each with the words to say which one failed.</summary>
    private static IEnumerable<(Booking Booking, string Label)> Cases()
    {
        var live = Build.RequestedBooking();
        yield return (live, "a request inside its answer window");

        var lapsedRequest = Build.RequestedBooking();
        yield return (lapsedRequest, "a request at the moment it was made");

        var approved = Build.ApprovedBooking();
        yield return (approved, "an approval inside its payment window");

        var cancelled = Build.RequestedBooking();
        cancelled.Cancel(BookingParty.Customer, Id.New(), "Plans changed.", Now.AddMinutes(1));
        yield return (cancelled, "a cancelled booking, whose deadlines no longer mean anything");
    }
}
