using CSharpFunctionalExtensions;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.Disputes;

public sealed class DisputeTicketTests
{
    private static readonly DateTimeOffset Now = Build.Now;
    private static readonly TimeSpan Sla = TimeSpan.FromHours(48);

    private static DisputeTicket OpenTicket(BookingParty? party = null, Id? opener = null) =>
        DisputeTicket.Open(
            Id.New(),
            opener ?? Id.New(),
            party ?? BookingParty.Customer,
            "The dealer never delivered the car.",
            Sla,
            Now,
            ["evidence/chat.png"]).Value;

    [Fact]
    public void Opening_a_ticket_starts_the_admin_clock_and_captures_the_first_statement()
    {
        var ticket = OpenTicket();

        Assert.Same(DisputeStatus.Open, ticket.Status);
        Assert.Equal(Now.Add(Sla), ticket.SlaDeadline);
        var statement = Assert.Single(ticket.Statements);
        Assert.Equal("The dealer never delivered the car.", statement.Body);
        Assert.Equal("evidence/chat.png", Assert.Single(statement.EvidenceStorageKeys));
    }

    [Fact]
    public void A_ticket_needs_a_reason()
    {
        var ticket = DisputeTicket.Open(Id.New(), Id.New(), BookingParty.Customer, "  ", Sla, Now);

        Assert.Equal("dispute.reason_required", ticket.Error.Code);
    }

    [Fact]
    public void Both_parties_argue_on_the_same_ticket()
    {
        var ticket = OpenTicket(BookingParty.Customer);

        Assert.True(ticket.AddStatement(BookingParty.Dealer, Id.New(), "The customer gave the wrong address.", Now.AddHours(1)).IsSuccess);

        Assert.Equal(2, ticket.Statements.Count);
        Assert.Contains(ticket.Statements, statement => statement.Party == BookingParty.Dealer);
    }

    [Fact]
    public void An_empty_statement_is_rejected()
    {
        var ticket = OpenTicket();

        Assert.Equal("dispute.statement_required", ticket.AddStatement(BookingParty.Dealer, Id.New(), " ", Now).Error.Code);
    }

    [Fact]
    public void Assigning_an_admin_moves_the_ticket_under_review_but_keeps_it_live()
    {
        var ticket = OpenTicket();
        var admin = Id.New();

        Assert.True(ticket.AssignToAdmin(admin).IsSuccess);

        Assert.Same(DisputeStatus.UnderReview, ticket.Status);
        Assert.True(ticket.Status.IsLive);
        Assert.Equal(admin, ticket.AssignedAdminId);
    }

    [Fact]
    public void The_sla_breach_only_applies_while_the_ticket_is_still_live()
    {
        var ticket = OpenTicket();

        Assert.False(ticket.IsBreachingSla(Now.AddHours(47)));
        Assert.True(ticket.IsBreachingSla(Now.AddHours(48)));

        var resolution = DisputeResolution.Create(
            DepositDisposition.RefundEverything(Money.Jod(18m)).Value, null, null, "Refunded.", Id.New(), Now).Value;
        ticket.Resolve(resolution);

        Assert.False(ticket.IsBreachingSla(Now.AddDays(30)));
    }

    [Fact]
    public void Resolving_records_the_decision_and_closes_the_ticket_for_good()
    {
        var ticket = OpenTicket();
        var admin = Id.New();
        // A dealer charge is a choice made INSIDE the range the booking already assessed, so the
        // resolution can only be built with that assessment in hand.
        var assessed = PenaltyAssessment.Range(
            BookingParty.Dealer,
            Percentage.FromValidated(25m),
            Percentage.FromValidated(50m),
            Money.Jod(100m),
            PenaltyReason.DealerDidNotHandOver,
            Now);

        var resolution = DisputeResolution.Create(
            DepositDisposition.RefundEverything(Money.Jod(18m)).Value,
            dealerCharge: Money.Jod(30m),
            assessedPenalty: assessed,
            "Dealer failed to deliver; customer refunded and dealer charged.",
            admin,
            Now.AddHours(5)).Value;

        Assert.True(ticket.Resolve(resolution).IsSuccess);

        Assert.Same(DisputeStatus.Resolved, ticket.Status);
        Assert.Equal(Now.AddHours(5), ticket.ClosedAt);
        Assert.Equal(Money.Jod(30m), ticket.Resolution!.DealerCharge);
        Assert.Equal("dispute.already_resolved", ticket.Resolve(resolution).Error.Code);
    }

    [Fact]
    public void Withdrawal_is_the_amicable_exit_and_only_the_opener_may_take_it()
    {
        var opener = Id.New();
        var ticket = OpenTicket(opener: opener);

        Assert.Equal("dispute.only_opener_can_withdraw", ticket.Withdraw(Id.New(), Now).Error.Code);
        Assert.True(ticket.Withdraw(opener, Now).IsSuccess);

        Assert.Same(DisputeStatus.Withdrawn, ticket.Status);
        Assert.False(ticket.Status.IsLive);
        Assert.Equal("dispute.not_open", ticket.AddStatement(BookingParty.Dealer, Id.New(), "wait", Now).Error.Code);
    }

    [Fact]
    public void A_withdrawn_ticket_cannot_then_be_resolved()
    {
        var opener = Id.New();
        var ticket = OpenTicket(opener: opener);
        ticket.Withdraw(opener, Now);
        var resolution = DisputeResolution.Create(
            DepositDisposition.RefundEverything(Money.Jod(18m)).Value, null, null, "n/a", Id.New(), Now).Value;

        Assert.Equal("dispute.already_withdrawn", ticket.Resolve(resolution).Error.Code);
    }
}

public sealed class DepositDispositionTests
{
    private static readonly Money Deposit = Money.Jod(18m);

    [Fact]
    public void Every_fils_of_the_held_deposit_must_be_accounted_for()
    {
        var unbalanced = DepositDisposition.Create(Deposit, Money.Jod(10m), Money.Jod(5m), Money.Jod(0m));

        Assert.Equal("dispute.disposition_unbalanced", unbalanced.Error.Code);
    }

    [Fact]
    public void A_three_way_split_is_allowed_when_it_balances()
    {
        var split = DepositDisposition.Create(Deposit, Money.Jod(6m), Money.Jod(6m), Money.Jod(6m));

        Assert.True(split.IsSuccess);
        Assert.Equal(Money.Jod(6m), split.Value.TransferredToDealer);
    }

    [Fact]
    public void A_full_refund_leaves_nothing_with_the_platform_or_the_dealer()
    {
        var refund = DepositDisposition.RefundEverything(Deposit).Value;

        Assert.Equal(Deposit, refund.RefundToCustomer);
        Assert.True(refund.RetainedByPlatform.IsZero);
        Assert.True(refund.TransferredToDealer.IsZero);
    }

    [Fact]
    public void Mixed_currencies_are_refused()
    {
        var mismatched = DepositDisposition.Create(
            Deposit, Money.Create(18m, "USD"), Money.Jod(0m), Money.Jod(0m));

        Assert.Equal("dispute.disposition_currency_mismatch", mismatched.Error.Code);
    }

    [Fact]
    public void A_resolution_must_explain_itself()
    {
        var resolution = DisputeResolution.Create(
            DepositDisposition.RefundEverything(Deposit).Value, null, null, "  ", Id.New(), Build.Now);

        Assert.Equal("dispute.resolution_note_required", resolution.Error.Code);
    }

    [Fact]
    public void A_full_refund_with_no_dealer_charge_waives_everything()
    {
        var resolution = DisputeResolution.Create(
            DepositDisposition.RefundEverything(Deposit).Value, null, null, "Amicable.", Id.New(), Build.Now).Value;

        Assert.True(resolution.WaivesEverything);
    }

    // A dealer charge is a choice inside a range the BOOKING fixed when the event happened. The
    // aggregate always said "an Admin picks inside it on a ticket"; these are the checks that make
    // that true rather than aspirational.
    private static PenaltyAssessment DealerPenalty() =>
        PenaltyAssessment.Range(
            BookingParty.Dealer,
            Percentage.FromValidated(25m),
            Percentage.FromValidated(50m),
            Money.Jod(100m),
            PenaltyReason.DealerDidNotHandOver,
            Build.Now);

    [Fact]
    public void A_dealer_charge_needs_a_penalty_the_booking_attributed_to_the_dealer()
    {
        var withoutAssessment = DisputeResolution.Create(
            DepositDisposition.RefundEverything(Deposit).Value,
            dealerCharge: Money.Jod(30m),
            assessedPenalty: null,
            "Charged.",
            Id.New(),
            Build.Now);

        var blamedTheCustomer = DisputeResolution.Create(
            DepositDisposition.RefundEverything(Deposit).Value,
            dealerCharge: Money.Jod(30m),
            assessedPenalty: PenaltyAssessment.Fixed(
                BookingParty.Customer,
                Percentage.FromValidated(100m),
                Money.Jod(40m),
                PenaltyReason.CustomerCancelledAfterFreeWindow,
                Build.Now),
            "Charged.",
            Id.New(),
            Build.Now);

        Assert.Equal("dispute.dealer_charge_unassessed", withoutAssessment.Error.Code);
        Assert.Equal("dispute.dealer_charge_unassessed", blamedTheCustomer.Error.Code);
    }

    [Theory]
    [InlineData(24)]
    [InlineData(51)]
    public void A_dealer_charge_outside_the_assessed_range_is_refused(decimal amount)
    {
        var resolution = DisputeResolution.Create(
            DepositDisposition.RefundEverything(Deposit).Value,
            dealerCharge: Money.Jod(amount),
            assessedPenalty: DealerPenalty(),
            "Charged.",
            Id.New(),
            Build.Now);

        Assert.Equal("dispute.dealer_charge_out_of_range", resolution.Error.Code);
    }

    [Theory]
    [InlineData(25)]
    [InlineData(37.5)]
    [InlineData(50)]
    public void A_dealer_charge_anywhere_inside_the_assessed_range_is_accepted(decimal amount)
    {
        var resolution = DisputeResolution.Create(
            DepositDisposition.RefundEverything(Deposit).Value,
            dealerCharge: Money.Jod(amount),
            assessedPenalty: DealerPenalty(),
            "Charged at the Admin's discretion inside the assessed band.",
            Id.New(),
            Build.Now);

        Assert.True(resolution.IsSuccess);
        Assert.Equal(amount, resolution.Value.DealerCharge!.Amount);
        Assert.False(resolution.Value.WaivesEverything);
    }

    // The range is the BOOKING's, not the ticket's (item 169): every charge on one booking together
    // stays inside it, so a later dispute can only top up to the maximum, and the minimum binds the
    // first charge only.
    private static Result<DisputeResolution, Error> ChargeAfter(decimal alreadyCharged, decimal charge) =>
        DisputeResolution.Create(
            DepositDisposition.Create(Money.Jod(0m), Money.Jod(0m), Money.Jod(0m), Money.Jod(0m)).Value,
            dealerCharge: Money.Jod(charge),
            assessedPenalty: DealerPenalty(),
            "A later dispute on the same booking.",
            Id.New(),
            Build.Now,
            alreadyChargedToDealer: Money.Jod(alreadyCharged));

    /// <summary>
    /// One statement of the office-charge rule (Wave 2 C1): the decision's preview asks the shared check before
    /// anything exists, the decision asks it again inside Create, and the two give the same verdict.
    /// </summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData(0.0, "dispute.dealer_charge_out_of_range")]
    [InlineData(25.0, null)]
    [InlineData(24.999, "dispute.dealer_charge_out_of_range")]
    [InlineData(50.001, "dispute.dealer_charge_out_of_range")]
    public void The_shared_charge_check_and_the_decision_give_the_same_verdict(double? charge, string? expected)
    {
        var money = charge is { } amount ? Money.Jod((decimal)amount) : null;

        var checkedAlone = DisputeResolution.CheckDealerCharge(money, DealerPenalty(), Money.Jod(0m));
        var decided = DisputeResolution.Create(
            DepositDisposition.RefundEverything(Deposit).Value,
            money,
            DealerPenalty(),
            "Decided.",
            Id.New(),
            Build.Now,
            Money.Jod(0m));

        Assert.Equal(expected, checkedAlone.IsFailure ? checkedAlone.Error.Code : null);
        Assert.Equal(expected, decided.IsFailure ? decided.Error.Code : null);
    }

    [Theory]
    [InlineData(25, 25)]    // the minimum first, then up to the maximum
    [InlineData(25, 1)]     // the floor bound only the first charge
    [InlineData(40, 10)]
    public void A_later_charge_may_top_up_to_the_maximum(decimal alreadyCharged, decimal charge) =>
        Assert.True(ChargeAfter(alreadyCharged, charge).IsSuccess);

    [Theory]
    [InlineData(25, 25.001)]
    [InlineData(40, 10.001)]
    [InlineData(50, 0.001)] // charged the maximum already: nothing more, ever
    public void A_later_charge_beyond_the_maximum_is_refused(decimal alreadyCharged, decimal charge) =>
        Assert.Equal("dispute.dealer_charge_out_of_range", ChargeAfter(alreadyCharged, charge).Error.Code);

    [Fact]
    public void What_earlier_disputes_charged_counts_only_in_the_ranges_currency()
    {
        var resolution = DisputeResolution.Create(
            DepositDisposition.Create(Money.Jod(0m), Money.Jod(0m), Money.Jod(0m), Money.Jod(0m)).Value,
            dealerCharge: Money.Jod(10m),
            assessedPenalty: DealerPenalty(),
            "A later dispute on the same booking.",
            Id.New(),
            Build.Now,
            alreadyChargedToDealer: Money.Create(25m, "USD"));

        Assert.Equal("dispute.dealer_charge_currency_mismatch", resolution.Error.Code);
    }

    [Fact]
    public void While_nothing_was_charged_the_minimum_still_binds()
    {
        Assert.Equal("dispute.dealer_charge_out_of_range", ChargeAfter(0m, 24m).Error.Code);
        Assert.True(ChargeAfter(0m, 25m).IsSuccess);
    }

    [Fact]
    public void A_disposition_keeps_the_basis_it_was_split_from()
    {
        // Payments reads this decision long after it was made and must be able to check the split
        // without re-deriving the basis from the very legs it is checking.
        var disposition = DepositDisposition.Create(
            Deposit,
            Money.Jod(10m),
            Money.Jod(8m),
            Money.ZeroIn("JOD")).Value;

        Assert.Equal(Deposit.Amount, disposition.DepositHeld.Amount);
        Assert.Equal("JOD", disposition.DepositHeld.CurrencyCode);
    }
}
