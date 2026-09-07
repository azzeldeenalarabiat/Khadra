using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Dealers.Dtos;
using Khadra.Domain.Auditing;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.Notifications;
using MediatR;

namespace Khadra.Application.Dealers.ReviewDealer;

// Spec 3.1: the Admin's licence check has THREE outcomes, not two. Approve, reject with a reason, or
// send it back with a specific note so the dealer can fix one thing rather than re-apply from scratch.
// Spec 3.2 adds suspend and reactivate for an already-approved business.
//
// All five live together because they are one decision surface: the same aggregate, the same guard,
// the same audit shape. Splitting them across five folders would scatter one idea and make it easy
// for the next one to forget the audit write.

public sealed record ApproveDealerCommand(Id DealerId) : ICommand<Result<DealerProfileDto, Error>>;

public sealed record RejectDealerCommand(Id DealerId, string Reason) : ICommand<Result<DealerProfileDto, Error>>;

public sealed record RequestDealerClarificationCommand(Id DealerId, string Note)
    : ICommand<Result<DealerProfileDto, Error>>;

public sealed record SuspendDealerCommand(Id DealerId, string Reason) : ICommand<Result<DealerProfileDto, Error>>;

public sealed record ReactivateDealerCommand(Id DealerId) : ICommand<Result<DealerProfileDto, Error>>;

public sealed class RejectDealerCommandValidator : AbstractValidator<RejectDealerCommand>
{
    public RejectDealerCommandValidator() =>
        RuleFor(command => command.Reason).NotEmpty().MaximumLength(1000);
}

public sealed class RequestDealerClarificationCommandValidator : AbstractValidator<RequestDealerClarificationCommand>
{
    public RequestDealerClarificationCommandValidator() =>
        RuleFor(command => command.Note).NotEmpty().MaximumLength(1000);
}

public sealed class SuspendDealerCommandValidator : AbstractValidator<SuspendDealerCommand>
{
    public SuspendDealerCommandValidator() =>
        RuleFor(command => command.Reason).NotEmpty().MaximumLength(1000);
}

/// <summary>
/// Every admin decision on a dealer follows the same three steps: load the aggregate, let it decide
/// whether the transition is legal, then record what happened. This holds that shape so no individual
/// outcome can quietly skip the audit write.
/// </summary>
public sealed class ReviewDealerHandlers(
    IDealerRepository dealers,
    DealerReviewAuditor auditor,
    Notifications.DealerTeamNotifier team,
    IClock clock,
    IUnitOfWork unitOfWork,
    ICurrentActor actor) :
    IRequestHandler<ApproveDealerCommand, Result<DealerProfileDto, Error>>,
    IRequestHandler<RejectDealerCommand, Result<DealerProfileDto, Error>>,
    IRequestHandler<RequestDealerClarificationCommand, Result<DealerProfileDto, Error>>,
    IRequestHandler<SuspendDealerCommand, Result<DealerProfileDto, Error>>,
    IRequestHandler<ReactivateDealerCommand, Result<DealerProfileDto, Error>>
{
    public Task<Result<DealerProfileDto, Error>> Handle(ApproveDealerCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return DecideAsync(
            request.DealerId,
            (dealer, adminId, now) => dealer.Approve(adminId, now),
            AuditAction.DealerApproved,
            NotificationKind.DealerApproved,
            reason: null,
            cancellationToken);
    }

    public Task<Result<DealerProfileDto, Error>> Handle(RejectDealerCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return DecideAsync(
            request.DealerId,
            (dealer, adminId, now) => dealer.Reject(adminId, request.Reason, now),
            AuditAction.DealerRejected,
            NotificationKind.DealerRejected,
            request.Reason,
            cancellationToken);
    }

    public Task<Result<DealerProfileDto, Error>> Handle(
        RequestDealerClarificationCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return DecideAsync(
            request.DealerId,
            (dealer, adminId, now) => dealer.RequestClarification(adminId, request.Note, now),
            AuditAction.DealerClarificationRequested,
            NotificationKind.DealerClarificationRequested,
            request.Note,
            cancellationToken);
    }

    public Task<Result<DealerProfileDto, Error>> Handle(SuspendDealerCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return DecideAsync(
            request.DealerId,
            (dealer, adminId, now) => dealer.Suspend(adminId, request.Reason, now),
            AuditAction.DealerSuspended,
            NotificationKind.DealerSuspended,
            request.Reason,
            cancellationToken);
    }

    public Task<Result<DealerProfileDto, Error>> Handle(
        ReactivateDealerCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return DecideAsync(
            request.DealerId,
            // Returns the aggregate's answer rather than discarding it: reactivating a dealership
            // that was never suspended used to succeed and write DealerReactivated into an
            // append-only table, where it can never be taken back.
            (dealer, _, _) => dealer.Reactivate(),
            AuditAction.DealerReactivated,
            NotificationKind.DealerReactivated,
            reason: null,
            cancellationToken);
    }

    private async Task<Result<DealerProfileDto, Error>> DecideAsync(
        Id dealerId,
        Func<Dealer, Id, DateTimeOffset, UnitResult<Error>> decide,
        AuditAction action,
        NotificationKind kind,
        string? reason,
        CancellationToken cancellationToken)
    {
        var dealer = await dealers.GetByIdAsync(dealerId, cancellationToken);
        if (dealer is null)
            return DealerErrors.NotRegistered;

        // Captured before the transition: an audit line that cannot say what the state WAS is only
        // half a record.
        var previousStatus = DealerReviewAuditor.DescribeStatus(dealer);

        // The aggregate owns which transitions are legal (spec 3.1: an approved dealer cannot be
        // rejected, a rejected one is not awaiting review, and so on).
        var decision = decide(dealer, actor.UserId ?? Id.Empty, clock.UtcNow);
        if (decision.IsFailure)
            return decision.Error;

        auditor.Record(dealer, action, previousStatus, reason);

        // The whole dealership is affected: an approval opens every screen, a suspension closes most
        // of them. Told to the owner AND to every active member of staff, because an employee's
        // console changes shape too and nothing else would explain why. Staged before the save, so
        // the decision and the notice commit in one transaction.
        await team.NotifyTeamAsync(
            dealer,
            actor.UserId ?? Id.Empty,
            kind,
            clock.UtcNow,
            dealer.Id,
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return DealerProfileDto.From(dealer);
    }
}

/// <summary>
/// Spec 3.1's middle outcome from the dealer's side: after a clarification request or a rejection the
/// owner fixes the problem and puts the application back in the queue.
///
/// This is the DEALER acting, not an Admin, so it takes no admin id and restarts the 48-hour SLA
/// clock from the moment of resubmission (Dealer.Resubmit freezes a fresh ReviewDueAt).
/// </summary>
public sealed record ResubmitDealerCommand(Id OwnerUserId) : ICommand<Result<DealerProfileDto, Error>>;

public sealed class ResubmitDealerHandler(
    IDealerRepository dealers,
    IBusinessRulesProvider businessRules,
    IClock clock,
    IUnitOfWork unitOfWork)
    : IRequestHandler<ResubmitDealerCommand, Result<DealerProfileDto, Error>>
{
    public async Task<Result<DealerProfileDto, Error>> Handle(
        ResubmitDealerCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var dealer = await dealers.GetByOwnerUserIdAsync(request.OwnerUserId, cancellationToken);
        if (dealer is null)
            return DealerErrors.NotRegistered;

        var rules = await businessRules.GetAsync(cancellationToken);
        var resubmitted = dealer.Resubmit(clock.UtcNow, TimeSpan.FromHours(rules.AdminSlaHours));
        if (resubmitted.IsFailure)
            return resubmitted.Error;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DealerProfileDto.From(dealer);
    }
}
