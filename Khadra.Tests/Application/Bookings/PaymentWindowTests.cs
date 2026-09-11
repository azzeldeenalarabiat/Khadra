using System.Text.Json;
using Khadra.Application.Bookings;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Tests.Support;

namespace Khadra.Tests.Application.Bookings;

/// <summary>
/// How long a customer has to pay the deposit once a gallery approves.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two hours</b>, settled by the owner on 2026-09-11, replacing the twenty-four that came in with
/// the reserve-now-pay-later reordering on 2026-09-07.
/// </para>
/// <para>
/// These tests exist because the platform now has TWO configured clocks of 120 minutes that mean
/// entirely different things: <c>PaymentWindowHours</c> is how long a deposit may go unpaid after an
/// APPROVAL, and <c>MinimumBookingLeadTimeMinutes</c> is how far ahead of NOW a rental may start.
/// They are the same length today and there is nothing in the type system to stop somebody wiring
/// one where the other belongs, so it is asserted rather than assumed.
/// </para>
/// </remarks>
public sealed class PaymentWindowTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    /// <summary>The rules as the PRICER freezes them, which is the only way a booking gets terms.</summary>
    private static async Task<BookingTerms> ConfiguredTermsAsync(
        int paymentWindowHours = TestBusinessRules.PaymentWindowHours,
        int minimumBookingLeadTimeMinutes = TestBusinessRules.MinimumBookingLeadTimeMinutes)
    {
        var dealer = Build.ApprovedDealer();
        var vehicle = Build.Vehicle(dealer.Id);
        var pricer = new BookingPricer(
            TestBusinessRules.Provider(
                paymentWindowHours: paymentWindowHours,
                minimumBookingLeadTimeMinutes: minimumBookingLeadTimeMinutes),
            TestBusinessRules.Calendar());

        var priced = await pricer.PriceAsync(
            vehicle, dealer, Build.Period(), PickupMethod.SelfPickup, deliveryLocation: null);

        Assert.True(priced.IsSuccess);
        return priced.Value.Terms;
    }

    private static async Task<Booking> ApprovableBookingAsync(DateRange? period = null) =>
        Build.Booking(Now, period: period, terms: await ConfiguredTermsAsync());

    /// <summary>The shipped configuration is the owner's two hours, not anything else.</summary>
    /// <remarks>
    /// Read from the file that actually ships. Every other test here can only prove that whatever
    /// number is configured is carried correctly — this is the one that proves WHICH number, and the
    /// number is the whole of the owner's decision.
    /// </remarks>
    [Fact]
    public void The_shipped_configuration_says_two_hours()
    {
        // Anchored on the file itself rather than on the solution, which has been renamed once
        // already (.sln to .slnx) and would have taken this test with it.
        var settingsPath = Path.Combine("Khadra.WebAPI", "appsettings.json");
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, settingsPath)))
            root = root.Parent;
        Assert.NotNull(root);

        // The file is JSON-with-comments, and nearly every number in it carries one saying who
        // decided it and when.
        using var settings = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root.FullName, settingsPath)),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var rules = settings.RootElement.GetProperty("BusinessRules");

        Assert.Equal(2, rules.GetProperty("PaymentWindowHours").GetInt32());
        // The lead time is asserted to EXIST and not to equal anything. Pinning its value here would
        // mean that the day the owner moves the lead time, the payment-window test fails — and the
        // person reading that failure edits the number in front of them. AppConfigTests owns its
        // value; what this file owns is that they are two settings.
        Assert.True(rules.TryGetProperty("MinimumBookingLeadTimeMinutes", out _));
    }

    /// <summary>The figure the pricer freezes is the PAYMENT window, not the lead time.</summary>
    [Fact]
    public async Task The_frozen_payment_window_is_the_payment_rule_and_not_the_lead_time()
    {
        // THREE, not the shipped two: a pricer that ignored the parameter entirely would return the
        // default and this test would pass on it. And 45 minutes for the lead time, so a pricer
        // reading the wrong clock cannot land on the right answer either.
        var terms = await ConfiguredTermsAsync(paymentWindowHours: 3, minimumBookingLeadTimeMinutes: 45);

        Assert.Equal(TimeSpan.FromHours(3), terms.PaymentWindow);
        Assert.NotEqual(TimeSpan.FromMinutes(45), terms.PaymentWindow);
        // And the gallery's clock is a third rule again: 48 hours to answer has nothing to do with
        // the customer's two hours to pay, and one screen has already confused those two.
        Assert.Equal(TimeSpan.FromHours(48), terms.AnswerWindow);
    }

    /// <summary>Approving opens exactly that window, measured from the approval.</summary>
    [Fact]
    public async Task Approval_gives_the_customer_two_hours_from_the_moment_it_happened()
    {
        var booking = await ApprovableBookingAsync();

        Assert.True(booking.Approve(Id.New(), Now.AddHours(6)).IsSuccess);

        // From the APPROVAL, not from the request: the customer could not have paid before there was
        // anything to pay for.
        Assert.Equal(Now.AddHours(8), booking.PaymentDeadline);
    }

    /// <summary>The window still cannot outlive the rental it is holding.</summary>
    /// <remarks>
    /// Shortening it to two hours makes this cap bite less often, not never: an approval ninety
    /// minutes before pickup still gives ninety minutes, not two hours.
    /// </remarks>
    [Fact]
    public async Task An_approval_close_to_pickup_gives_only_the_time_that_is_left()
    {
        var start = Now.AddMinutes(90);
        var period = DateRange.Create(start, start.AddDays(2)).Value;
        var booking = await ApprovableBookingAsync(period);

        Assert.True(booking.Approve(Id.New(), Now).IsSuccess);

        Assert.Equal(start, booking.PaymentDeadline);
    }

    /// <summary>Nothing expires until the window has actually elapsed, and then it does.</summary>
    /// <remarks>
    /// The settlement pass runs on a clock of its own (every 60 seconds), so what matters here is
    /// that the aggregate refuses an early expiry and accepts a due one — the sweep only supplies
    /// <c>now</c>. Two hours leaves the payment machinery the same room it always had: a checkout
    /// session lives 30 minutes and closes 5 minutes before the deadline, both inside the window.
    /// </remarks>
    [Fact]
    public async Task The_booking_expires_when_the_two_hours_are_up_and_not_a_minute_before()
    {
        var booking = await ApprovableBookingAsync();
        booking.Approve(Id.New(), Now);

        var deadline = booking.PaymentDeadline!.Value;
        Assert.Equal(Now.AddHours(2), deadline);

        Assert.Equal(
            "booking.payment_window_open",
            booking.ExpireUnpaid(deadline.AddSeconds(-1)).Error.Code);

        Assert.True(booking.ExpireUnpaid(deadline).IsSuccess);
        Assert.Same(BookingStatus.Expired, booking.Status);
        // Nobody is at fault and nothing is owed: no deposit was ever taken.
        Assert.NotNull(booking.Penalty);
        Assert.True(booking.Penalty.IsNothingOwed);
    }

    /// <summary>A deposit paid inside the window confirms, and the deadline stops mattering.</summary>
    [Fact]
    public async Task Paying_inside_the_window_confirms_the_booking()
    {
        var booking = await ApprovableBookingAsync();
        booking.Approve(Id.New(), Now);

        Assert.True(booking.ConfirmDepositPaid(Id.New(), Now.AddMinutes(119)).IsSuccess);

        Assert.Same(BookingStatus.Confirmed, booking.Status);
        Assert.Equal(
            "booking.not_awaiting_payment",
            booking.ExpireUnpaid(Now.AddHours(3)).Error.Code);
    }

    /// <summary>
    /// The free-cancellation window runs from the PAYMENT, and the shorter clock does not swallow it.
    /// </summary>
    /// <remarks>
    /// Worth restating now the payment window is the shorter of the two. Under twenty-four hours a
    /// customer could pay at hour 23 and find their hour of free cancellation had closed twenty-two
    /// hours earlier, which is why it was moved onto the payment; at two hours the same code still
    /// has to give them the hour.
    /// </remarks>
    [Fact]
    public async Task Free_cancellation_is_measured_from_the_payment_not_the_approval()
    {
        var booking = await ApprovableBookingAsync();
        booking.Approve(Id.New(), Now);

        var paidAt = Now.AddMinutes(115);
        Assert.True(booking.ConfirmDepositPaid(Id.New(), paidAt).IsSuccess);

        Assert.Equal(paidAt.AddHours(1), booking.FreeCancellationDeadline);
    }
}
