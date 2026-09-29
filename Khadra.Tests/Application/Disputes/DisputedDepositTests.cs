using Khadra.Application.Disputes;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Khadra.Tests.Support;

namespace Khadra.Tests.Application.Disputes;

// The one calculator of what a dispute may still split (owner, 2026-09-26; pre-launch item 169): the
// booking's deposit less what its EARLIER resolved disputes decided. The resolve handler validates a
// decision against it and the view shows it, so these are the arithmetic both stand on.
public sealed class DisputedDepositTests
{
    private static readonly Id AdminId = Id.New();

    /// <summary>A booking the customer cancelled late after paying its 18.000 JOD deposit.</summary>
    private static Booking CancelledBooking()
    {
        var booking = Build.ConfirmedBooking(terms: Build.Terms(settlementWindow: TimeSpan.FromDays(7)));
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, "Changed plans.", Build.Now.AddHours(3)).IsSuccess);
        booking.ClearDomainEvents();
        Assert.Equal(18m, booking.Pricing.DepositAmount.Amount);
        return booking;
    }

    private static DisputeTicket Ticket(Booking booking, int hours) =>
        DisputeTicket.Open(booking.Id, booking.CustomerId, BookingParty.Customer, "Something went wrong.", TimeSpan.FromHours(48), Build.Now.AddHours(hours)).Value;

    /// <summary>
    /// A ticket resolved with the given split of the given basis. The handler always decides the whole
    /// of its basis today, so a basis smaller than the deposit is how a PARTIAL decision reads here.
    /// </summary>
    private static DisputeTicket Resolved(Booking booking, int hours, decimal basis, decimal refund, decimal platform, decimal dealer, decimal? dealerCharge = null, decimal alreadyCharged = 0m)
    {
        var ticket = Ticket(booking, hours);
        var resolution = DisputeResolution.Create(
            DepositDisposition.Create(Money.Jod(basis), Money.Jod(refund), Money.Jod(platform), Money.Jod(dealer)).Value,
            dealerCharge is { } charge ? Money.Jod(charge) : null,
            dealerCharge is null ? null : PenaltyAssessment.Range(
                BookingParty.Dealer, Percentage.FromValidated(25m), Percentage.FromValidated(50m), Money.Jod(100m),
                PenaltyReason.DealerDidNotHandOver, Build.Now),
            "Decided.",
            AdminId,
            Build.Now.AddHours(hours + 1),
            Money.Jod(alreadyCharged)).Value;
        Assert.True(ticket.Resolve(resolution).IsSuccess);
        return ticket;
    }

    [Fact]
    public void A_first_dispute_may_split_the_whole_deposit()
    {
        var booking = CancelledBooking();

        var basis = DisputedDeposit.For(Ticket(booking, 4), booking, depositReleased: false, []).Value;

        Assert.Equal(18m, basis.DepositOnBooking.Amount);
        Assert.Equal(0m, basis.DecidedByEarlierTickets.Amount);
        Assert.Equal(18m, basis.Basis.Amount);
        Assert.Equal("JOD", basis.Basis.CurrencyCode);
    }

    /// <summary>The owner's first example: 18.000 decided, so the next dispute has 0.000.</summary>
    [Fact]
    public void After_the_whole_deposit_was_decided_a_second_dispute_has_nothing_to_split()
    {
        var booking = CancelledBooking();
        var first = Resolved(booking, 4, 18m, 9m, 0m, 9m);

        var basis = DisputedDeposit.For(Ticket(booking, 8), booking, depositReleased: false, [first]).Value;

        Assert.Equal(18m, basis.DepositOnBooking.Amount);
        Assert.Equal(18m, basis.DecidedByEarlierTickets.Amount);
        Assert.Equal(0m, basis.Basis.Amount);
        Assert.Equal("dispute.disposition_unbalanced", DepositDisposition.Create(basis.Basis, Money.Jod(0.001m), Money.Jod(0m), Money.Jod(0m)).Error.Code);
        Assert.True(DepositDisposition.Create(basis.Basis, Money.Jod(0m), Money.Jod(0m), Money.Jod(0m)).IsSuccess);
    }

    /// <summary>
    /// The owner's second example: 5.000 decided, so the next dispute can only operate on the
    /// remaining 13.000 — and every fils of THAT must be decided, never the original 18.000.
    /// </summary>
    [Fact]
    public void After_a_partial_decision_only_the_remainder_can_be_split()
    {
        var booking = CancelledBooking();
        var first = Resolved(booking, 4, 5m, 0m, 0m, 5m);

        var basis = DisputedDeposit.For(Ticket(booking, 8), booking, depositReleased: false, [first]).Value;

        Assert.Equal(18m, basis.DepositOnBooking.Amount);
        Assert.Equal(5m, basis.DecidedByEarlierTickets.Amount);
        Assert.Equal(13m, basis.Basis.Amount);
        // Over the remainder: refused. The original deposit: refused. The remainder: accepted.
        Assert.Equal("dispute.disposition_unbalanced", DepositDisposition.Create(basis.Basis, Money.Jod(14m), Money.Jod(0m), Money.Jod(0m)).Error.Code);
        Assert.Equal("dispute.disposition_unbalanced", DepositDisposition.Create(basis.Basis, Money.Jod(18m), Money.Jod(0m), Money.Jod(0m)).Error.Code);
        var second = DepositDisposition.Create(basis.Basis, Money.Jod(8m), Money.Jod(0m), Money.Jod(5m));
        Assert.True(second.IsSuccess);
        // Both decisions together allocate exactly the deposit, never more.
        Assert.Equal(18m, basis.DecidedByEarlierTickets.Amount + second.Value.DepositHeld.Amount);
    }

    [Fact]
    public void Decisions_add_up_across_every_earlier_ticket()
    {
        var booking = CancelledBooking();
        var tickets = new[] { Resolved(booking, 4, 5m, 5m, 0m, 0m), Resolved(booking, 6, 3m, 0m, 3m, 0m) };

        var basis = DisputedDeposit.For(Ticket(booking, 8), booking, depositReleased: false, tickets).Value;

        Assert.Equal(8m, basis.DecidedByEarlierTickets.Amount);
        Assert.Equal(10m, basis.Basis.Amount);
    }

    /// <summary>
    /// Only EARLIER decisions on THIS booking count: not the ticket itself, not one opened after it, not
    /// another booking's. A resolved ticket keeps the basis it was decided against.
    /// </summary>
    [Fact]
    public void Only_earlier_decisions_on_the_same_booking_count()
    {
        var booking = CancelledBooking();
        var other = CancelledBooking();
        var asked = Resolved(booking, 6, 18m, 18m, 0m, 0m);
        var later = Resolved(booking, 8, 0m, 0m, 0m, 0m);
        var foreign = Resolved(other, 4, 18m, 18m, 0m, 0m);

        var basis = DisputedDeposit.For(asked, booking, depositReleased: false, [asked, later, foreign]).Value;

        Assert.Equal(0m, basis.DecidedByEarlierTickets.Amount);
        Assert.Equal(18m, basis.Basis.Amount);
    }

    /// <summary>
    /// Pre-launch item 171: a live ticket counts every resolved ticket of its booking, even one whose clock says it
    /// opened later — two nodes' clocks can disagree, and only one ticket per booking can ever be live.
    /// </summary>
    [Fact]
    public void A_live_ticket_counts_every_resolved_ticket_whatever_the_clocks_say()
    {
        var booking = CancelledBooking();
        var skewed = Resolved(booking, 12, 18m, 9m, 0m, 9m);
        var live = Ticket(booking, 8);
        Assert.True(skewed.OpenedAt > live.OpenedAt);

        var basis = DisputedDeposit.For(live, booking, depositReleased: false, [skewed]).Value;

        Assert.Equal(18m, basis.DecidedByEarlierTickets.Amount);
        Assert.Equal(0m, basis.Basis.Amount);
    }

    /// <summary>Pre-launch item 171: a resolved ticket reads its own stored basis, never the clocks.</summary>
    [Fact]
    public void A_resolved_ticket_keeps_the_basis_it_was_decided_against_whatever_the_clocks_say()
    {
        var booking = CancelledBooking();
        var first = Resolved(booking, 8, 18m, 18m, 0m, 0m);
        var second = Resolved(booking, 4, 0m, 0m, 0m, 0m);
        Assert.True(second.OpenedAt < first.OpenedAt);

        var ofFirst = DisputedDeposit.For(first, booking, depositReleased: false, [first, second]).Value;
        var ofSecond = DisputedDeposit.For(second, booking, depositReleased: false, [first, second]).Value;

        Assert.Equal((18m, 0m), (ofFirst.Basis.Amount, ofFirst.DecidedByEarlierTickets.Amount));
        Assert.Equal((0m, 18m), (ofSecond.Basis.Amount, ofSecond.DecidedByEarlierTickets.Amount));
    }

    [Fact]
    public void A_withdrawn_or_live_ticket_decided_nothing()
    {
        var booking = CancelledBooking();
        var withdrawn = Ticket(booking, 4);
        Assert.True(withdrawn.Withdraw(booking.CustomerId, Build.Now.AddHours(5)).IsSuccess);
        var live = Ticket(booking, 6);

        var basis = DisputedDeposit.For(Ticket(booking, 8), booking, depositReleased: false, [withdrawn, live]).Value;

        Assert.Equal(0m, basis.DecidedByEarlierTickets.Amount);
        Assert.Equal(18m, basis.Basis.Amount);
    }

    /// <summary>More decided than was ever held is a data fault, refused rather than floored at zero.</summary>
    [Fact]
    public void Earlier_decisions_beyond_the_deposit_are_refused()
    {
        var booking = CancelledBooking();
        var tickets = new[] { Resolved(booking, 4, 18m, 18m, 0m, 0m), Resolved(booking, 6, 0.001m, 0.001m, 0m, 0m) };

        var basis = DisputedDeposit.For(Ticket(booking, 8), booking, depositReleased: false, tickets);

        Assert.Equal("dispute.deposit_over_allocated", basis.Error.Code);
    }

    [Fact]
    public void A_released_deposit_leaves_nothing_on_the_booking_to_split()
    {
        var booking = CancelledBooking();

        var basis = DisputedDeposit.For(Ticket(booking, 4), booking, depositReleased: true, []).Value;

        Assert.Equal(0m, basis.DepositOnBooking.Amount);
        Assert.Equal(0m, basis.Basis.Amount);
    }

    [Fact]
    public void What_earlier_decisions_charged_the_office_is_carried_to_the_next()
    {
        var booking = CancelledBooking();
        var tickets = new[] { Resolved(booking, 4, 18m, 18m, 0m, 0m, dealerCharge: 25m), Resolved(booking, 6, 0m, 0m, 0m, 0m, dealerCharge: 10m, alreadyCharged: 25m) };

        var basis = DisputedDeposit.For(Ticket(booking, 8), booking, depositReleased: false, tickets).Value;

        Assert.Equal(35m, basis.ChargedToDealerEarlier.Amount);
    }

    [Fact]
    public void An_earlier_charge_to_the_office_in_another_currency_is_a_programming_error()
    {
        var booking = CancelledBooking();
        var earlier = Ticket(booking, 4);
        Assert.True(earlier.Resolve(DisputeResolution.Create(
            DepositDisposition.RefundEverything(Money.Jod(18m)).Value,
            Money.Create(30m, "USD"),
            PenaltyAssessment.Range(
                BookingParty.Dealer, Percentage.FromValidated(25m), Percentage.FromValidated(50m), Money.Create(100m, "USD"),
                PenaltyReason.DealerDidNotHandOver, Build.Now),
            "Decided.",
            AdminId,
            Build.Now.AddHours(5)).Value).IsSuccess);

        Assert.Throws<DomainException>(() => DisputedDeposit.For(Ticket(booking, 8), booking, depositReleased: false, [earlier]));
    }

    [Fact]
    public void A_decision_in_another_currency_is_a_programming_error()
    {
        var booking = CancelledBooking();
        var foreignCurrency = Ticket(booking, 4);
        Assert.True(foreignCurrency.Resolve(DisputeResolution.Create(
            DepositDisposition.RefundEverything(Money.Create(18m, "USD")).Value, null, null, "Decided.", AdminId, Build.Now.AddHours(5)).Value).IsSuccess);

        Assert.Throws<DomainException>(() => DisputedDeposit.For(Ticket(booking, 8), booking, depositReleased: false, [foreignCurrency]));
    }
}
