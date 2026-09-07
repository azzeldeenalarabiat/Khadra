using Khadra.Application.Notifications;
using Khadra.Domain.Notifications.Repositories;
using Khadra.Domain.IdentityAccess.Repositories;
using Khadra.Application.Common;
using Khadra.Application.Dealers;
using Khadra.Application.Dealers.ReviewDealer;
using Khadra.Domain.Auditing;
using Khadra.Domain.Auditing.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.IdentityAccess;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.Dealers;

// Spec 3.1: three outcomes, not two. Spec 3.2: suspend and reactivate an approved business. Every one
// of them must leave an audit line, because these are the decisions the platform most needs to be
// able to account for later.
public sealed class DealerReviewTests
{
    private static readonly Id AdminId = Id.New();

    private sealed class Context
    {
        public IDealerRepository Dealers { get; } = Substitute.For<IDealerRepository>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public IAuditTrail AuditTrail { get; } = Substitute.For<IAuditTrail>();
        public ICurrentActor Actor { get; } = Substitute.For<ICurrentActor>();
        public TestClock Clock { get; } = new(Users.Now);
        public List<AuditEntry> Recorded { get; } = [];

        public Context()
        {
            UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
            AuditTrail.When(trail => trail.Record(Arg.Any<AuditEntry>()))
                .Do(call => Recorded.Add(call.Arg<AuditEntry>()));
            Actor.UserId.Returns(AdminId);
            Actor.Role.Returns(UserRole.Admin);
            Actor.Name.Returns("Rania Haddad");
            Actor.CorrelationId.Returns("test-correlation");
        }

        public Dealer Given(Dealer dealer)
        {
            Dealers.GetByIdAsync(dealer.Id, Arg.Any<CancellationToken>()).Returns(dealer);
            Dealers.GetByOwnerUserIdAsync(dealer.OwnerUserId, Arg.Any<CancellationToken>()).Returns(dealer);
            return dealer;
        }

        public ReviewDealerHandlers Handlers() => new(
            Dealers,
            new DealerReviewAuditor(AuditTrail, Actor, Clock),
            new DealerTeamNotifier(Substitute.For<INotifier>(), Substitute.For<IUserRepository>()),
            Clock,
            UnitOfWork,
            Actor);

        public ResubmitDealerHandler Resubmit() =>
            new(Dealers, TestBusinessRules.Provider(), Clock, UnitOfWork);
    }

    private static Dealer PendingWithDocuments()
    {
        var dealer = Build.Dealer();
        Build.AttachAllDocuments(dealer);
        return dealer;
    }

    [Fact]
    public async Task Approving_lets_the_dealer_trade_and_records_the_decision()
    {
        var context = new Context();
        var dealer = context.Given(PendingWithDocuments());

        var result = await context.Handlers().Handle(new ApproveDealerCommand(dealer.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Approved", result.Value.VerificationStatus);
        Assert.True(result.Value.CanTrade);

        var entry = Assert.Single(context.Recorded);
        Assert.Same(AuditAction.DealerApproved, entry.Action);
        Assert.Equal(AdminId, entry.ActorUserId);
        Assert.Equal("Rania Haddad", entry.ActorName);
        Assert.Equal("PendingReview", entry.PreviousValue);
        Assert.Equal("Approved", entry.NewValue);
        Assert.Equal(dealer.BusinessName.Value, entry.SubjectLabel);
    }

    [Fact]
    public async Task An_application_missing_documents_cannot_be_approved_and_nothing_is_recorded()
    {
        // Spec 3.1 lists all three as required evidence; the aggregate refuses, and a refused decision
        // must not leave an audit line claiming one was made.
        var context = new Context();
        var dealer = context.Given(Build.Dealer());

        var result = await context.Handlers().Handle(new ApproveDealerCommand(dealer.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("dealer.missing_documents", result.Error.Code);
        Assert.Empty(context.Recorded);
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rejecting_records_the_reason_the_admin_gave()
    {
        var context = new Context();
        var dealer = context.Given(PendingWithDocuments());

        var result = await context.Handlers().Handle(
            new RejectDealerCommand(dealer.Id, "The commercial registration has expired."), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Rejected", result.Value.VerificationStatus);
        Assert.False(result.Value.CanTrade);
        Assert.Equal("The commercial registration has expired.", result.Value.ReviewNote);

        var entry = Assert.Single(context.Recorded);
        Assert.Same(AuditAction.DealerRejected, entry.Action);
        Assert.Equal("The commercial registration has expired.", entry.Reason);
    }

    [Fact]
    public async Task Clarification_sends_it_back_with_a_note_rather_than_ending_the_application()
    {
        // The whole point of the third outcome: the dealer fixes one thing instead of re-applying.
        var context = new Context();
        var dealer = context.Given(PendingWithDocuments());

        var result = await context.Handlers().Handle(
            new RequestDealerClarificationCommand(dealer.Id, "The vehicle registration photo is unreadable."),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("ClarificationNeeded", result.Value.VerificationStatus);
        Assert.Equal("The vehicle registration photo is unreadable.", result.Value.ReviewNote);
        Assert.Same(AuditAction.DealerClarificationRequested, Assert.Single(context.Recorded).Action);
    }

    [Fact]
    public async Task An_approved_dealer_cannot_be_approved_or_rejected_again()
    {
        var context = new Context();
        var dealer = context.Given(Build.ApprovedDealer());

        Assert.Equal("dealer.already_approved",
            (await context.Handlers().Handle(new ApproveDealerCommand(dealer.Id), CancellationToken.None)).Error.Code);
        Assert.Equal("dealer.already_approved",
            (await context.Handlers().Handle(new RejectDealerCommand(dealer.Id, "changed my mind"), CancellationToken.None)).Error.Code);
        Assert.Empty(context.Recorded);
    }

    [Fact]
    public async Task Suspension_stops_trading_without_undoing_the_licence_check()
    {
        // Spec 3.2: suspension is a policy sanction. The dealer stays Approved so reactivating does
        // not send them back through review.
        var context = new Context();
        var dealer = context.Given(Build.ApprovedDealer());

        var suspended = await context.Handlers().Handle(
            new SuspendDealerCommand(dealer.Id, "Unresolved disputes."), CancellationToken.None);

        Assert.True(suspended.IsSuccess);
        Assert.Equal("Approved", suspended.Value.VerificationStatus);
        Assert.True(suspended.Value.IsSuspended);
        Assert.False(suspended.Value.CanTrade);
        Assert.Equal("Approved", context.Recorded[0].PreviousValue);
        Assert.Equal("Suspended", context.Recorded[0].NewValue);

        var reactivated = await context.Handlers().Handle(
            new ReactivateDealerCommand(dealer.Id), CancellationToken.None);

        Assert.True(reactivated.IsSuccess);
        Assert.True(reactivated.Value.CanTrade);
        Assert.Equal("Suspended", context.Recorded[1].PreviousValue);
        Assert.Equal("Approved", context.Recorded[1].NewValue);
        Assert.Same(AuditAction.DealerReactivated, context.Recorded[1].Action);
    }

    [Fact]
    public async Task A_decision_on_a_dealer_that_does_not_exist_is_a_not_found()
    {
        var context = new Context();

        var result = await context.Handlers().Handle(new ApproveDealerCommand(Id.New()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("dealer.not_registered", result.Error.Code);
        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
    }

    [Fact]
    public async Task Resubmission_puts_it_back_in_the_queue_and_restarts_the_sla_clock()
    {
        var context = new Context();
        var dealer = PendingWithDocuments();
        dealer.RequestClarification(AdminId, "Unreadable photo.", Users.Now.AddHours(-5));
        context.Given(dealer);

        var result = await context.Resubmit().Handle(
            new ResubmitDealerCommand(dealer.OwnerUserId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("PendingReview", result.Value.VerificationStatus);
        Assert.Null(result.Value.ReviewNote);
        // A fresh 48-hour promise from the moment of resubmission, not the original submission.
        Assert.Equal(Users.Now.AddHours(48), result.Value.ReviewDueAt);
    }

    [Fact]
    public async Task An_approved_application_has_nothing_to_resubmit()
    {
        var context = new Context();
        var dealer = context.Given(Build.ApprovedDealer());

        var result = await context.Resubmit().Handle(
            new ResubmitDealerCommand(dealer.OwnerUserId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("dealer.nothing_to_resubmit", result.Error.Code);
    }

    [Fact]
    public async Task Every_recorded_decision_carries_the_correlation_id_of_the_request()
    {
        var context = new Context();
        var dealer = context.Given(PendingWithDocuments());

        await context.Handlers().Handle(new ApproveDealerCommand(dealer.Id), CancellationToken.None);

        Assert.Equal("test-correlation", Assert.Single(context.Recorded).CorrelationId);
    }
}
