using Khadra.Application.Reviews.ReadModels;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Khadra.Domain.Reviews;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Reporting;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Tests.Persistence;

/// <summary>
/// What a gallery is told about a customer, against the real EF model.
/// </summary>
/// <remarks>
/// Two things are being proved. First that the queries TRANSLATE at all: the penalty is a
/// <c>ToJson()</c> value object, and a projection or predicate over one is exactly the shape that
/// works on Postgres and throws on SQLite. Second, and more important, that the counts blame the
/// right party — the platform is careful about attribution in three places, and a reader that
/// counted by status would undo all three.
/// </remarks>
public sealed class CustomerReputationReaderTests : IDisposable
{
    private static readonly DateTimeOffset Now = Build.Now;

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;
    private readonly Id _customerId = Id.New();
    private readonly Id _dealerId = Id.New();

    public CustomerReputationReaderTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<KhadraDbContext>()
            .UseSqlite(_connection)
            .UseSnakeCaseNamingConvention()
            .Options;
        using var context = new KhadraDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private KhadraDbContext NewContext() => new(_options);

    private Task<CustomerReputation> ReadAsync(DateTimeOffset? at = null)
    {
        var context = NewContext();
        return new CustomerReputationReader(context).GetAsync(_customerId, _dealerId, at ?? Now);
    }

    private async Task SaveAsync(params Booking[] bookings)
    {
        await using var context = NewContext();
        context.Bookings.AddRange(bookings);
        await context.SaveChangesAsync();
    }

    private Booking Completed(Id? dealerId = null)
    {
        var booking = Build.ConfirmedBooking(Now, customerId: _customerId, dealerId: dealerId ?? _dealerId);
        booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.Period.Start);
        booking.RecordReturn(BookingParty.Dealer, Id.New(), booking.Period.End);
        booking.Settle(booking.Period.End.Add(booking.Terms.PostReturnSettlementWindow), hasOpenDispute: false);
        return booking;
    }

    /// <summary>An account nobody has rented to reads as absent, not as bad.</summary>
    [Fact]
    public async Task A_customer_with_no_history_has_none()
    {
        var reputation = await ReadAsync();

        Assert.False(reputation.HasHistory);
        Assert.Equal(0, reputation.CompletedRentals);
        // Null, never 0: zero is a real score on a one-to-five scale and would read as the worst.
        Assert.Null(reputation.DealerRating.Average);
        Assert.Equal(0, reputation.DealerRating.Count);
    }

    [Fact]
    public async Task Completed_rentals_are_counted_overall_and_for_the_asking_gallery()
    {
        await SaveAsync(Completed(), Completed(), Completed(dealerId: Id.New()));

        var reputation = await ReadAsync();

        Assert.Equal(3, reputation.CompletedRentals);
        Assert.Equal(2, reputation.CompletedRentalsWithThisDealer);
        Assert.True(reputation.HasHistory);
    }

    /// <summary>
    /// The whole point of reading <c>Penalty.AttributedTo</c> rather than the status.
    /// </summary>
    /// <remarks>
    /// A DELIVERY no-show is <c>Unattributed</c>: the gallery was the party who had to travel, and the
    /// aggregate deliberately refuses to blame the customer for it. Counting <c>Status == NoShow</c>
    /// would put it on the customer's permanent record anyway.
    /// </remarks>
    [Fact]
    public async Task A_delivery_no_show_is_not_counted_against_the_customer()
    {
        var selfPickup = Build.ConfirmedBooking(Now, customerId: _customerId, dealerId: _dealerId);
        selfPickup.MarkNoShow(selfPickup.Period.Start.Add(selfPickup.Terms.NoShowTimeout));

        var delivery = Build.ConfirmedBooking(
            Now, pickupMethod: PickupMethod.Delivery, customerId: _customerId, dealerId: _dealerId);
        delivery.MarkNoShow(delivery.Period.Start.Add(delivery.Terms.NoShowTimeout));

        await SaveAsync(selfPickup, delivery);

        var reputation = await ReadAsync();

        Assert.Same(BookingStatus.NoShow, selfPickup.Status);
        Assert.Same(BookingStatus.NoShow, delivery.Status);
        Assert.Same(BookingParty.Unattributed, delivery.Penalty!.AttributedTo);
        // Two no-show rows, ONE of them the customer's fault.
        Assert.Equal(1, reputation.NoShows);
    }

    /// <summary>
    /// The sharpest of the three. <c>ReportDealerNonDelivery</c> cancels a booking with
    /// <c>CancelledBy = Customer</c> while attributing the penalty to the DEALER — it is the customer
    /// reporting that the gallery failed. Counting cancellations by who pressed the button would put
    /// the gallery's failure on the customer's record.
    /// </summary>
    [Fact]
    public async Task Reporting_that_a_gallery_never_delivered_is_not_a_mark_against_the_customer()
    {
        var booking = Build.ConfirmedBooking(Now, customerId: _customerId, dealerId: _dealerId);
        var due = booking.Period.Start.Add(booking.Terms.NonDeliveryGrace);
        booking.ReportDealerNonDelivery(_customerId, "Nobody came with the car.", due);

        await SaveAsync(booking);

        var reputation = await ReadAsync();

        Assert.Same(BookingStatus.Cancelled, booking.Status);
        Assert.Same(BookingParty.Customer, booking.CancelledBy);
        Assert.Same(BookingParty.Dealer, booking.Penalty!.AttributedTo);
        Assert.Equal(0, reputation.LateCancellations);
    }

    /// <summary>A cancellation inside the free window costs nothing and says nothing.</summary>
    [Fact]
    public async Task A_free_cancellation_is_not_counted()
    {
        var free = Build.ConfirmedBooking(Now, customerId: _customerId, dealerId: _dealerId);
        free.Cancel(BookingParty.Customer, _customerId, "changed plans", Now);

        var late = Build.ConfirmedBooking(Now, customerId: _customerId, dealerId: _dealerId);
        late.Cancel(
            BookingParty.Customer,
            _customerId,
            "changed plans",
            late.FreeCancellationDeadline!.Value.AddMinutes(1));

        await SaveAsync(free, late);

        var reputation = await ReadAsync();

        Assert.True(free.Penalty!.IsNothingOwed);
        Assert.Same(BookingParty.Unattributed, free.Penalty.AttributedTo);
        Assert.Same(BookingParty.Customer, late.Penalty!.AttributedTo);
        Assert.Equal(1, reputation.LateCancellations);
    }

    /// <summary>
    /// A gallery declining a request, or a platform expiring one nobody paid, says nothing about the
    /// person who asked.
    /// </summary>
    [Fact]
    public async Task Rejected_and_expired_bookings_leave_no_mark()
    {
        var rejected = Build.Booking(Now, customerId: _customerId, dealerId: _dealerId);
        rejected.Reject(Id.New(), BookingRejectionReason.VehicleUnavailable, "Car is in for service.", Now);

        var expired = Build.ApprovedBooking(Now, customerId: _customerId, dealerId: _dealerId);
        expired.ExpireUnpaid(expired.PaymentDeadline!.Value);

        await SaveAsync(rejected, expired);

        var reputation = await ReadAsync();

        Assert.False(reputation.HasHistory);
        Assert.Equal(0, reputation.LateCancellations);
        Assert.Equal(0, reputation.NoShows);
    }

    // ---------------------------------------------------------------- the blind window

    private async Task RateAsync(int rating, DateTimeOffset visibleFrom, bool hidden = false)
    {
        var review = Review.Leave(
            Id.New(),
            ReviewDirection.DealerRatesCustomer,
            Id.New(),
            _customerId,
            Rating.Create(rating).Value,
            comment: null,
            bookingIsCompleted: true,
            revealAt: visibleFrom,
            now: visibleFrom.AddDays(-1)).Value;
        if (hidden)
            review.Hide("Retaliatory.");

        await using var context = NewContext();
        context.Reviews.Add(review);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Without this the blind window is decorative: a gallery would read the counterpart here and
    /// answer it before the other party could see anything.
    /// </summary>
    [Fact]
    public async Task A_rating_inside_its_blind_window_does_not_count()
    {
        await RateAsync(5, visibleFrom: Now.AddDays(-1));
        await RateAsync(1, visibleFrom: Now.AddDays(7));

        var reputation = await ReadAsync();

        Assert.Equal(1, reputation.DealerRating.Count);
        Assert.Equal(5m, reputation.DealerRating.Average);
    }

    /// <summary>
    /// The OPPOSITE of the public direction, and the asymmetry is the point. A gallery's public score
    /// survives moderation so it cannot erase a bad rating by reporting the comment on it. Here there
    /// is no comment: the only thing an administrator can hide is the score, and the only reason to
    /// hide it is that it was wrong.
    /// </summary>
    [Fact]
    public async Task A_hidden_rating_does_not_count_against_a_customer()
    {
        await RateAsync(4, visibleFrom: Now.AddDays(-1));
        await RateAsync(1, visibleFrom: Now.AddDays(-1), hidden: true);

        var reputation = await ReadAsync();

        Assert.Equal(1, reputation.DealerRating.Count);
        Assert.Equal(4m, reputation.DealerRating.Average);
        Assert.False(ReviewDirection.DealerRatesCustomer.HiddenScoreStillCounts);
        Assert.True(ReviewDirection.CustomerRatesDealer.HiddenScoreStillCounts);
    }

    [Fact]
    public async Task The_average_is_rounded_to_the_one_decimal_it_is_displayed_to()
    {
        await RateAsync(5, visibleFrom: Now.AddDays(-1));
        await RateAsync(4, visibleFrom: Now.AddDays(-1));
        await RateAsync(4, visibleFrom: Now.AddDays(-1));

        var reputation = await ReadAsync();

        Assert.Equal(3, reputation.DealerRating.Count);
        Assert.Equal(4.3m, reputation.DealerRating.Average);
    }

    private async Task ResolveDisputeAsync(Id bookingId, DepositDisposition disposition)
    {
        var ticket = DisputeTicket
            .Open(bookingId, _customerId, BookingParty.Customer, "I was there.", TimeSpan.FromHours(48), Now)
            .Value;
        ticket.Resolve(
            DisputeResolution.Create(disposition, null, null, "Settled.", Id.New(), Now.AddHours(2)).Value);

        await using var context = NewContext();
        context.DisputeTickets.Add(ticket);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// A no-show the customer DISPUTED AND WON does not stay on their record.
    /// </summary>
    /// <remarks>
    /// It did until 2026-09-11, and this is the case that makes it matter. The assessment is what the
    /// booking closed with; a resolution returning the whole deposit is the platform saying that
    /// assessment was wrong. Leaving the count standing punished a customer for having been right, in
    /// the one place they could not see, in front of the one audience deciding whether to rent to
    /// them — and made winning the dispute pointless, since it moved the money and left the mark.
    ///
    /// The reader already had this reasoning written on <c>WentAgainstCustomer</c>, for the disputes
    /// counter. It simply was not applied to the other two.
    /// </remarks>
    [Fact]
    public async Task A_no_show_the_customer_disputed_and_won_stops_counting_against_them()
    {
        var booking = Build.ConfirmedBooking(Now, customerId: _customerId, dealerId: _dealerId);
        booking.MarkNoShow(booking.Period.Start.Add(booking.Terms.NoShowTimeout));
        await SaveAsync(booking);

        // Before the ticket: the assessment stands and the count is honest.
        Assert.Equal(1, (await ReadAsync()).NoShows);

        await ResolveDisputeAsync(
            booking.Id,
            DepositDisposition.RefundEverything(booking.Pricing.DepositAmount).Value);

        var reputation = await ReadAsync();

        Assert.Same(BookingParty.Customer, booking.Penalty!.AttributedTo);
        Assert.Equal(0, reputation.NoShows);
        // And it is not silently moved into the other column either: a ticket the customer WON is
        // not a dispute that went against them.
        Assert.Equal(0, reputation.DisputesResolvedAgainstCustomer);
    }

    /// <summary>A ticket the customer LOST leaves the record exactly as it was.</summary>
    [Fact]
    public async Task A_dispute_that_went_against_the_customer_leaves_the_no_show_standing()
    {
        var booking = Build.ConfirmedBooking(Now, customerId: _customerId, dealerId: _dealerId);
        booking.MarkNoShow(booking.Period.Start.Add(booking.Terms.NoShowTimeout));
        await SaveAsync(booking);

        var held = booking.Pricing.DepositAmount;
        await ResolveDisputeAsync(
            booking.Id,
            DepositDisposition.Create(
                Money.Create(held.Amount, held.CurrencyCode),
                Money.ZeroIn(held.CurrencyCode),
                Money.ZeroIn(held.CurrencyCode),
                Money.Create(held.Amount, held.CurrencyCode)).Value);

        var reputation = await ReadAsync();

        Assert.Equal(1, reputation.NoShows);
        Assert.Equal(1, reputation.DisputesResolvedAgainstCustomer);
    }

    /// <summary>
    /// An OPEN ticket clears nothing. A record must not go quiet merely because it is being argued.
    /// </summary>
    [Fact]
    public async Task An_unresolved_dispute_does_not_clear_a_no_show()
    {
        var booking = Build.ConfirmedBooking(Now, customerId: _customerId, dealerId: _dealerId);
        booking.MarkNoShow(booking.Period.Start.Add(booking.Terms.NoShowTimeout));
        await SaveAsync(booking);

        var ticket = DisputeTicket
            .Open(booking.Id, _customerId, BookingParty.Customer, "I was there.", TimeSpan.FromHours(48), Now)
            .Value;

        await using (var context = NewContext())
        {
            context.DisputeTickets.Add(ticket);
            await context.SaveChangesAsync();
        }

        var reputation = await ReadAsync();

        Assert.Equal(1, reputation.NoShows);
        Assert.Equal(0, reputation.DisputesResolvedAgainstCustomer);
    }
}
