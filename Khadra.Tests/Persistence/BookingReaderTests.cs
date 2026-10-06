using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Khadra.Domain.Payments;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Reporting;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Tests.Persistence;

// The dealer's tabs are resolved in the database, and the list and its counts share one mapping.
// These pin that mapping to real rows: a tab and its count can never disagree, an unpaid request is
// exactly what the dealer is shown, and "Disputed" cuts across status rather than being one.
public sealed class BookingReaderTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;
    private readonly Id _dealerId = Id.New();

    public BookingReaderTests()
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

    private Booking Mine(Action<Booking>? advance = null)
    {
        var booking = Build.Booking(dealerId: _dealerId);
        advance?.Invoke(booking);
        return booking;
    }

    [Fact]
    public async Task Tabs_and_their_counts_agree_and_an_unpaid_request_is_what_the_dealer_must_answer()
    {
        var requested = Mine();
        // Answered but not paid for, and answered and paid for. Both are ahead of their pickup, so
        // both are the dealer's week: one tab, two statuses on the rows.
        var approved = Mine(b => b.Approve(Id.New(), Build.Now));
        var confirmed = Mine(b => { b.Approve(Id.New(), Build.Now); b.ConfirmDepositPaid(Id.New(), Build.Now); });
        var cancelled = Mine(b =>
        {
            b.Approve(Id.New(), Build.Now);
            b.ConfirmDepositPaid(Id.New(), Build.Now);
            b.Cancel(BookingParty.Customer, Id.New(), "Plans changed.", Build.Now.AddHours(3));
        });
        var returned = Mine(b =>
        {
            b.Approve(Id.New(), Build.Now);
            b.ConfirmDepositPaid(Id.New(), Build.Now);
            b.RecordPickup(BookingParty.Dealer, Id.New(), b.Period.Start);
            b.RecordReturn(BookingParty.Dealer, Id.New(), b.Period.End);
        });
        var ticket = DisputeTicket.Open(returned.Id, returned.CustomerId, BookingParty.Customer, "Overcharged.", TimeSpan.FromHours(48), Build.Now.AddDays(4)).Value;

        await using (var context = NewContext())
        {
            context.Bookings.AddRange(requested, approved, confirmed, cancelled, returned);
            context.DisputeTickets.Add(ticket);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var bookings = new BookingReader(reader);
        var scope = new BookingListFilter(null, _dealerId, null);

        var counts = await bookings.TabCountsAsync(scope);
        Assert.Equal(5, counts[BookingTabs.All]);          // including the request nobody has paid for
        Assert.Equal(1, counts[BookingTabs.Pending]);
        // Two statuses, one tab. This is also the regression that mattered: a tab naming more than
        // one status used to fall through to the whole scope and count 5.
        Assert.Equal(2, counts[BookingTabs.Upcoming]);
        Assert.Equal(0, counts[BookingTabs.Active]);
        Assert.Equal(1, counts[BookingTabs.Returned]);
        Assert.Equal(1, counts[BookingTabs.Closed]);
        Assert.Equal(1, counts[BookingTabs.Disputed]);

        foreach (var tab in BookingTabs.Names)
        {
            var page = await bookings.ListAsync(scope with { Tab = tab }, PageRequest.From(1, 50));
            Assert.Equal(counts[tab], page.TotalCount);
        }

        var disputed = await bookings.ListAsync(scope with { Tab = BookingTabs.Disputed }, PageRequest.From(1, 50));
        Assert.Equal(returned.Id.Value, disputed.Items.Single().BookingId);
        Assert.True(disputed.Items.Single().HasLiveDispute);
        Assert.Equal("Returned", disputed.Items.Single().Status);
    }

    [Fact]
    public async Task A_customer_still_sees_their_own_unpaid_booking()
    {
        var customerId = Id.New();
        var unpaid = Build.Booking(customerId: customerId);

        await using (var context = NewContext())
        {
            context.Bookings.Add(unpaid);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var counts = await new BookingReader(reader).TabCountsAsync(new BookingListFilter(customerId, null, null));

        Assert.Equal(1, counts[BookingTabs.All]);
    }

    [Fact]
    public async Task A_vehicle_filter_narrows_the_dealer_scope_to_that_car_only()
    {
        var carA = Id.New();
        var carB = Id.New();
        var onA = Build.Booking(dealerId: _dealerId, vehicleId: carA);
        onA.Approve(Id.New(), Build.Now);
        var onB = Build.Booking(dealerId: _dealerId, vehicleId: carB);
        onB.Approve(Id.New(), Build.Now);
        // Same car, another dealer: the vehicle filter never widens the caller's scope.
        var elsewhere = Build.Booking(dealerId: Id.New(), vehicleId: carA);
        elsewhere.Approve(Id.New(), Build.Now);

        await using (var context = NewContext())
        {
            context.Bookings.AddRange(onA, onB, elsewhere);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var page = await new BookingReader(reader).ListAsync(
            new BookingListFilter(null, _dealerId, null, VehicleId: carA.Value), PageRequest.From(1, 20));

        var only = Assert.Single(page.Items);
        Assert.Equal(onA.Id.Value, only.BookingId);
    }

    /// <summary>
    /// A row says when the office approved it (Wave 3, F65), so the customer's copy can keep the plate back until then
    /// and the office's copy, which never passes through that rule, keeps it whatever.
    /// </summary>
    [Fact]
    public async Task A_row_says_when_the_office_approved_it()
    {
        var waiting = Mine();
        var approved = Mine(booking => booking.Approve(Id.New(), Build.Now));

        await using (var context = NewContext())
        {
            context.Bookings.AddRange(waiting, approved);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var page = await new BookingReader(reader).ListAsync(new BookingListFilter(null, _dealerId, null), PageRequest.From(1, 20));

        Assert.Null(page.Items.Single(row => row.BookingId == waiting.Id.Value).ApprovedAt);
        Assert.Equal(Build.Now, page.Items.Single(row => row.BookingId == approved.Id.Value).ApprovedAt);
    }

    private KhadraDbContext NewContext() => new(_options);

    // ── The landing surface's one booking ────────────────────────────────────

    private readonly Id _customerId = Id.New();

    private Task<NextBooking?> NextAsync()
    {
        var context = NewContext();
        return new BookingReader(context).NextForCustomerAsync(_customerId, Build.Now);
    }

    private async Task SaveMineAsync(params Booking[] bookings)
    {
        await using var context = NewContext();
        context.Bookings.AddRange(bookings);
        await context.SaveChangesAsync();
    }

    /// <summary>Most people, most of the time. The screen renders nothing, never a placeholder.</summary>
    [Fact]
    public async Task A_customer_with_nothing_live_has_no_next_booking()
    {
        // Somebody else's live booking must not surface on this customer's landing.
        await SaveMineAsync(Build.ApprovedBooking(customerId: Id.New(), dealerId: _dealerId));

        Assert.Null(await NextAsync());
    }

    /// <summary>
    /// The ranking, and the reason it is the server's to make.
    /// </summary>
    /// <remarks>
    /// With no push channel (pre-launch checklist item 73) a customer learns their booking was
    /// approved by opening the app -- and since 2026-09-11 they have two hours to pay. An approval
    /// owing a deposit therefore outranks a rental starting tomorrow, and an app sorting by pickup
    /// date would have shown the rental and let the deposit expire unread.
    /// </remarks>
    [Fact]
    public async Task A_deposit_that_is_due_outranks_everything_else()
    {
        var awaitingPayment = Build.ApprovedBooking(customerId: _customerId, dealerId: _dealerId);
        var startingSooner = Build.ConfirmedBooking(customerId: _customerId, dealerId: _dealerId);
        var unanswered = Build.Booking(customerId: _customerId, dealerId: _dealerId);

        await SaveMineAsync(startingSooner, unanswered, awaitingPayment);

        var next = await NextAsync();

        Assert.NotNull(next);
        Assert.Equal(awaitingPayment.Id.Value, next.Booking.BookingId);
        Assert.Equal(NextBookingReason.AwaitingPayment, next.Reason);
    }

    [Fact]
    public async Task A_car_already_out_outranks_one_not_yet_collected()
    {
        var collected = Build.ConfirmedBooking(customerId: _customerId, dealerId: _dealerId);
        collected.RecordPickup(BookingParty.Dealer, Id.New(), collected.Period.Start);
        var later = Build.ConfirmedBooking(customerId: _customerId, dealerId: _dealerId);

        await SaveMineAsync(later, collected);

        var next = await NextAsync();

        Assert.Equal(collected.Id.Value, next!.Booking.BookingId);
        Assert.Equal(NextBookingReason.InProgress, next.Reason);
    }

    [Fact]
    public async Task An_unanswered_request_is_the_last_thing_shown_and_still_shown()
    {
        var unanswered = Build.Booking(customerId: _customerId, dealerId: _dealerId);
        await SaveMineAsync(unanswered);

        var next = await NextAsync();

        Assert.Equal(unanswered.Id.Value, next!.Booking.BookingId);
        Assert.Equal(NextBookingReason.AwaitingDecision, next.Reason);
    }

    /// <summary>Of two of the same kind, the one that needs attention SOONEST.</summary>
    [Fact]
    public async Task The_soonest_of_two_upcoming_rentals_wins_whatever_order_they_were_booked_in()
    {
        var later = Build.ConfirmedBooking(
            now: Build.Now.AddDays(30), customerId: _customerId, dealerId: _dealerId);
        var sooner = Build.ConfirmedBooking(customerId: _customerId, dealerId: _dealerId);

        // Saved with the LATER one first, so a fallback to insertion order would pick it.
        await SaveMineAsync(later, sooner);

        var next = await NextAsync();

        Assert.Equal(sooner.Id.Value, next!.Booking.BookingId);
        Assert.True(sooner.Period.Start < later.Period.Start);
    }

    /// <summary>A booking that has ended is not "next" by any reading.</summary>
    [Fact]
    public async Task A_finished_or_cancelled_booking_is_never_next()
    {
        var cancelled = Build.ConfirmedBooking(customerId: _customerId, dealerId: _dealerId);
        cancelled.Cancel(BookingParty.Customer, _customerId, "changed plans", Build.Now);

        var expired = Build.ApprovedBooking(customerId: _customerId, dealerId: _dealerId);
        expired.ExpireUnpaid(expired.PaymentDeadline!.Value.AddMinutes(1));

        await SaveMineAsync(cancelled, expired);

        Assert.Null(await NextAsync());
    }

    // ── Parties that no longer resolve ───────────────────────────────────────
    //
    // A booking outlives its dealership and its customer. The name fields stay strings, with an
    // English stand-in, because shipped customer apps print them as they arrive; the flags are what a
    // client that words the case in its reader's language reads instead. Both are taken from ONE read
    // of each name, so a flag and its stand-in can never disagree.

    /// <summary>A dealership and a customer that both still resolve: real names, no flag.</summary>
    [Fact]
    public async Task Parties_that_still_resolve_are_named_and_raise_no_flag()
    {
        var (dealer, customer, booking) = await SaveWithPartiesAsync(deleteDealer: false, deleteCustomer: false);

        await using var read = NewContext();
        var reader = new BookingReader(read);

        var context = await reader.ContextAsync(booking.Id);
        Assert.Equal(dealer.BusinessName.Value, context.DealerName);
        Assert.False(context.DealerRemoved);
        Assert.Equal(customer.Name.Value, context.CustomerName);
        Assert.False(context.CustomerAccountClosed);

        var row = Assert.Single((await reader.ListAsync(new BookingListFilter(null, dealer.Id, null), PageRequest.From(1, 50))).Items);
        Assert.Equal(dealer.BusinessName.Value, row.DealerName);
        Assert.False(row.DealerRemoved);
        Assert.Equal(customer.Name.Value, row.CustomerName);
        Assert.False(row.CustomerAccountClosed);
    }

    /// <summary>
    /// A dealership that left the platform and a customer who closed their account: each says so by
    /// its flag, and the stand-in older apps print is still there.
    /// </summary>
    [Fact]
    public async Task Parties_that_no_longer_resolve_are_flagged_and_keep_the_stand_in()
    {
        var (dealer, _, booking) = await SaveWithPartiesAsync(deleteDealer: true, deleteCustomer: true);

        await using var read = NewContext();
        var reader = new BookingReader(read);

        var context = await reader.ContextAsync(booking.Id);
        Assert.True(context.DealerRemoved);
        Assert.Equal("Dealer no longer on the platform", context.DealerName);
        Assert.True(context.CustomerAccountClosed);
        Assert.Equal("Customer account closed", context.CustomerName);

        var row = Assert.Single((await reader.ListAsync(new BookingListFilter(null, dealer.Id, null), PageRequest.From(1, 50))).Items);
        Assert.True(row.DealerRemoved);
        Assert.Equal("Dealer no longer on the platform", row.DealerName);
        Assert.True(row.CustomerAccountClosed);
        Assert.Equal("Customer account closed", row.CustomerName);
    }

    /// <summary>One gone and one not: the flags are independent, not one "something is missing" bit.</summary>
    [Fact]
    public async Task Each_party_is_flagged_on_its_own()
    {
        var (dealer, customer, booking) = await SaveWithPartiesAsync(deleteDealer: false, deleteCustomer: true);

        await using var read = NewContext();
        var context = await new BookingReader(read).ContextAsync(booking.Id);

        Assert.False(context.DealerRemoved);
        Assert.Equal(dealer.BusinessName.Value, context.DealerName);
        Assert.True(context.CustomerAccountClosed);
        Assert.NotEqual(customer.Name.Value, context.CustomerName);
    }

    private async Task<(Khadra.Domain.Dealers.Dealer Dealer, Khadra.Domain.IdentityAccess.User Customer, Booking Booking)> SaveWithPartiesAsync(
        bool deleteDealer,
        bool deleteCustomer)
    {
        var dealer = Build.ApprovedDealer(businessName: "Wadi Rum Rentals", commercialRegistration: "778899");
        var customer = Build.Customer(email: "parties@example.jo", phone: "0797654321");
        var booking = Build.Booking(customerId: customer.Id, dealerId: dealer.Id);

        if (deleteDealer)
            Assert.True(dealer.Delete(Build.Now).IsSuccess);
        if (deleteCustomer)
        {
            Assert.True(customer.Delete(Build.Now).IsSuccess);
            customer.ClearDomainEvents();
        }

        await using var write = NewContext();
        write.Dealers.Add(dealer);
        write.Users.Add(customer);
        write.Bookings.Add(booking);
        await write.SaveChangesAsync();

        return (dealer, customer, booking);
    }

    /// <summary>
    /// The deposit a free cancellation returned (owner, 2026-09-24) reaches every reader of the
    /// booking with the refund's own status, and follows it as the provider answers.
    /// </summary>
    /// <summary>
    /// "Has a dispute resolved this booking's penalty" is read from the tickets themselves (pre-launch
    /// item 173): only a RESOLVED ticket counts. An open or under-review one has not decided yet, and a
    /// withdrawn one decided nothing.
    /// </summary>
    [Theory]
    [InlineData("None", false)]
    [InlineData("Open", false)]
    [InlineData("UnderReview", false)]
    [InlineData("Withdrawn", false)]
    [InlineData("Resolved", true)]
    public async Task Only_a_resolved_dispute_counts_as_having_resolved_the_booking(string ticketState, bool expected)
    {
        var booking = Build.ConfirmedBooking(dealerId: _dealerId);
        var cancelledAt = booking.FreeCancellationDeadline!.Value.AddMinutes(1);
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, "Changed plans.", cancelledAt).IsSuccess);
        var ticket = ticketState == "None"
            ? null
            : DisputeTicket.Open(booking.Id, booking.CustomerId, BookingParty.Customer, "The penalty is wrong.", TimeSpan.FromHours(48), cancelledAt.AddHours(1)).Value;
        switch (ticketState)
        {
            case "UnderReview":
                Assert.True(ticket!.AssignToAdmin(Id.New()).IsSuccess);
                break;
            case "Withdrawn":
                Assert.True(ticket!.Withdraw(booking.CustomerId, cancelledAt.AddHours(2)).IsSuccess);
                break;
            case "Resolved":
                Assert.True(ticket!.Resolve(DisputeResolution.Create(
                    DepositDisposition.RefundEverything(Money.Create(booking.Pricing.DepositAmount.Amount, booking.Pricing.CurrencyCode)).Value,
                    null, null, "Refunded.", Id.New(), cancelledAt.AddHours(3)).Value).IsSuccess);
                break;
        }

        await using (var write = NewContext())
        {
            write.Bookings.Add(booking);
            if (ticket is not null)
                write.DisputeTickets.Add(ticket);
            await write.SaveChangesAsync();
        }

        await using var read = NewContext();
        var context = await new BookingReader(read).ContextAsync(booking.Id);

        Assert.Equal(expected, context.HasResolvedDispute);
    }

    /// <summary>
    /// Every dispute on a booking, live or closed, oldest first (Wave 3 C3; E2E F44): a withdrawn first ticket and a
    /// second one still open are both listed, each with its status and when it closed.
    /// </summary>
    [Fact]
    public async Task Every_dispute_on_a_booking_is_listed_oldest_first_whatever_its_state()
    {
        var booking = Build.ConfirmedBooking(dealerId: _dealerId);
        var cancelledAt = booking.FreeCancellationDeadline!.Value.AddMinutes(1);
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, "Changed plans.", cancelledAt).IsSuccess);
        var first = DisputeTicket.Open(booking.Id, booking.CustomerId, BookingParty.Customer, "The penalty is wrong.", TimeSpan.FromHours(48), cancelledAt.AddHours(1)).Value;
        Assert.True(first.Withdraw(booking.CustomerId, cancelledAt.AddHours(2)).IsSuccess);
        var second = DisputeTicket.Open(booking.Id, booking.CustomerId, BookingParty.Customer, "On reflection, it is.", TimeSpan.FromHours(48), cancelledAt.AddHours(3)).Value;

        await using (var write = NewContext())
        {
            write.Bookings.Add(booking);
            write.DisputeTickets.AddRange(second, first);
            await write.SaveChangesAsync();
        }

        await using var read = NewContext();
        var disputes = (await new BookingReader(read).ContextAsync(booking.Id)).Disputes!;

        Assert.Equal([first.Id.Value, second.Id.Value], disputes.Select(dispute => dispute.TicketId));
        Assert.Equal(["Withdrawn", "Open"], disputes.Select(dispute => dispute.Status));
        Assert.Equal(cancelledAt.AddHours(2), disputes[0].ClosedAt);
        Assert.Null(disputes[1].ClosedAt);
    }

    [Fact]
    public async Task A_free_cancellations_refund_is_read_with_its_status_and_follows_it()
    {
        var booking = Build.ApprovedBooking(Build.Now, dealerId: _dealerId);
        var deposit = Money.Create(booking.Pricing.DepositAmount.Amount, booking.Pricing.CurrencyCode);
        var payment = Payment.Open(booking.Id, booking.CustomerId, deposit, "TestProvider", Build.Now.AddMinutes(30), Build.Now);
        payment.AttachProviderSession("sess_reader", "https://provider.test/sess_reader");
        Assert.True(payment.Apply(Money.Create(deposit.Amount, deposit.CurrencyCode), Build.Now, Build.Now).IsSuccess);
        Assert.True(booking.ConfirmDepositPaid(payment.Id, Build.Now).IsSuccess);
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, Build.Now).IsSuccess);
        var refund = payment.RefundForFreeCancellation(Build.Now).Value;

        await using (var write = NewContext())
        {
            write.Bookings.Add(booking);
            write.Payments.Add(payment);
            await write.SaveChangesAsync();
        }

        await using (var read = NewContext())
        {
            var context = await new BookingReader(read).ContextAsync(booking.Id);
            Assert.NotNull(context.DepositRefund);
            Assert.Equal("Requested", context.DepositRefund.Status);
            Assert.Equal(deposit.Amount, context.DepositRefund.Amount.Amount);
            Assert.Equal(deposit.CurrencyCode, context.DepositRefund.Amount.Currency);
            Assert.Null(context.DepositRefund.SettledAt);
        }

        await using (var write = NewContext())
        {
            var stored = await write.Payments.Include(p => p.Refunds).SingleAsync(p => p.Id == payment.Id);
            stored.FreeCancellationRefund!.MarkSent("rf_reader", Build.Now.AddMinutes(1));
            stored.FreeCancellationRefund!.MarkSettled(Build.Now.AddMinutes(5));
            await write.SaveChangesAsync();
        }

        await using (var read = NewContext())
        {
            var context = await new BookingReader(read).ContextAsync(booking.Id);
            Assert.Equal("Settled", context.DepositRefund!.Status);
            Assert.Equal(Build.Now.AddMinutes(5), context.DepositRefund.SettledAt);
        }
    }

    /// <summary>
    /// The payment that confirmed a booking reaches every reader with its purpose and what it charged
    /// (owner, 2026-09-25), so a full payment is never worded as a deposit on any screen.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_confirming_payment_is_read_with_its_purpose_and_what_it_charged(bool full)
    {
        var booking = Build.ApprovedBooking(Build.Now, dealerId: _dealerId);
        var currency = booking.Pricing.CurrencyCode;
        var applied = full ? booking.Pricing.TotalPrice.Amount : booking.Pricing.DepositAmount.Amount;
        // A non-refundable fee on the full payment, so the free-cancellation figure is not simply the
        // charge: it has to come from the payment's own rule.
        var fee = full ? 1.5m : 0m;
        var payment = Payment.Open(
            booking.Id, booking.CustomerId, Money.Create(applied + fee, currency), "TestProvider", Build.Now.AddMinutes(30), Build.Now,
            full ? PaymentPurpose.FullPayment : PaymentPurpose.Deposit, Money.Create(fee, currency), feeRefundable: false);
        payment.AttachProviderSession("sess_confirming", "https://provider.test/sess_confirming");
        Assert.True(payment.Apply(Money.Create(applied + fee, currency), Build.Now, Build.Now.AddMinutes(1)).IsSuccess);
        Assert.True(booking.ConfirmPayment(payment.Id, payment.AppliedToBooking, Build.Now.AddMinutes(1)).IsSuccess);

        await using (var write = NewContext())
        {
            write.Bookings.Add(booking);
            write.Payments.Add(payment);
            await write.SaveChangesAsync();
        }

        await using var read = NewContext();
        var confirming = (await new BookingReader(read).ContextAsync(booking.Id)).ConfirmingPayment;

        Assert.NotNull(confirming);
        Assert.Equal(full ? "FullPayment" : "Deposit", confirming.Purpose);
        // The reader composes every figure; only the office's copy of a booking drops the fee-bearing ones.
        Assert.NotNull(confirming.AmountCharged);
        Assert.NotNull(confirming.ProcessingFee);
        Assert.NotNull(confirming.RefundOnFreeCancellation);
        Assert.Equal(applied + fee, confirming.AmountCharged.Amount);
        Assert.Equal(currency, confirming.AmountCharged.Currency);
        Assert.Equal(fee, confirming.ProcessingFee.Amount);
        Assert.Equal(applied, confirming.AppliedToBooking.Amount);
        Assert.Equal(applied, confirming.RefundOnFreeCancellation.Amount);
        Assert.Equal(Build.Now.AddMinutes(1), confirming.PaidAt);
    }

    /// <summary>A booking no payment on record confirmed carries no confirming payment, rather than a guessed one.</summary>
    [Fact]
    public async Task A_booking_with_no_confirming_payment_on_record_reads_none()
    {
        var booking = Build.ConfirmedBooking(Build.Now, dealerId: _dealerId);
        await using (var write = NewContext())
        {
            write.Bookings.Add(booking);
            await write.SaveChangesAsync();
        }

        await using var read = NewContext();
        Assert.Null((await new BookingReader(read).ContextAsync(booking.Id)).ConfirmingPayment);
    }

    /// <summary>A booking with nothing refunded carries no refund, rather than an empty one.</summary>
    [Fact]
    public async Task A_booking_with_no_refund_reads_none()
    {
        var booking = Build.ConfirmedBooking(Build.Now, dealerId: _dealerId);
        await using (var write = NewContext())
        {
            write.Bookings.Add(booking);
            await write.SaveChangesAsync();
        }

        await using var read = NewContext();
        Assert.Null((await new BookingReader(read).ContextAsync(booking.Id)).DepositRefund);
    }
}
