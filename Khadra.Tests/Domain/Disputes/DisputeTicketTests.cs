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
            DepositDisposition.RefundEverything(Money.Jod(18m)).Value, null, "Refunded.", Id.New(), Now).Value;
        ticket.Resolve(resolution);

        Assert.False(ticket.IsBreachingSla(Now.AddDays(30)));
    }

    [Fact]
    public void Resolving_records_the_decision_and_closes_the_ticket_for_good()
    {
        var ticket = OpenTicket();
        var admin = Id.New();
        var resolution = DisputeResolution.Create(
            DepositDisposition.RefundEverything(Money.Jod(18m)).Value,
            dealerCharge: Money.Jod(30m),
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
            DepositDisposition.RefundEverything(Money.Jod(18m)).Value, null, "n/a", Id.New(), Now).Value;

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
            DepositDisposition.RefundEverything(Deposit).Value, null, "  ", Id.New(), Build.Now);

        Assert.Equal("dispute.resolution_note_required", resolution.Error.Code);
    }

    [Fact]
    public void A_full_refund_with_no_dealer_charge_waives_everything()
    {
        var resolution = DisputeResolution.Create(
            DepositDisposition.RefundEverything(Deposit).Value, null, "Amicable.", Id.New(), Build.Now).Value;

        Assert.True(resolution.WaivesEverything);
    }
}
