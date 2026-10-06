using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Bookings;
using Khadra.Application.Common;
using Khadra.Application.Common.Dtos;
using Khadra.Application.Disputes.Dtos;
using Khadra.Application.Disputes.ReadModels;
using Khadra.Application.Payables.Dtos;
using Khadra.Application.Payables.ReadModels;
using Khadra.Application.Payments.Financials;
using Khadra.Domain.Auditing;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.Disputes;
using Khadra.Domain.Disputes.Repositories;
using Khadra.Domain.Payments;
using Khadra.Domain.Payments.Repositories;
using Khadra.Application.Notifications;
using Khadra.Domain.Notifications;
using MediatR;

namespace Khadra.Application.Disputes.ResolveDispute;

// The Admin's side of spec 3.3: the queue, the workspace, taking a ticket on, and the decision.
// Every act here that changes anything is written to the append-only audit trail in the SAME
// transaction as the change.

public sealed record ListDisputesQuery(string? Status, bool OverdueOnly, int? Page, int? PageSize)
    : IQuery<Result<PagedResult<DisputeListItem>, Error>>;

/// <summary>How the queue is shaped under the same filters, for all of it rather than one page.</summary>
public sealed record GetDisputeQueueCountsQuery(string? Status, bool OverdueOnly)
    : IQuery<Result<DisputeQueueCounts, Error>>;

public sealed record GetDisputeForReviewQuery(Id TicketId) : IQuery<Result<DisputeDto, Error>>;

public sealed record AssignDisputeCommand(Id TicketId) : ICommand<Result<DisputeDto, Error>>;

/// <summary>
/// The decision, as three legs of the held deposit plus an optional dealer charge, in the booking's
/// currency. The held amount is NOT accepted from the client: the server reads it from the booking's
/// frozen pricing, and the three legs must add up to exactly that.
/// </summary>
public sealed record ResolveDisputeCommand(
    Id TicketId,
    decimal RefundToCustomer,
    decimal RetainedByPlatform,
    decimal TransferredToDealer,
    decimal? DealerCharge,
    string Note) : ICommand<Result<DisputeDto, Error>>;

/// <summary>
/// What a decision WOULD do, before it is made (Wave 2 C1; E2E F37): every check resolving makes, then the office's
/// money as the ledger would record it. Writes nothing. A query carried by POST, because it takes the decision's
/// whole body. The note is optional, so the preview can follow the amounts while they are still being typed.
/// </summary>
public sealed record PreviewDisputeResolutionQuery(
    Id TicketId,
    decimal RefundToCustomer,
    decimal RetainedByPlatform,
    decimal TransferredToDealer,
    decimal? DealerCharge,
    string? Note) : IQuery<Result<ResolutionPreviewDto, Error>>;

public sealed class ListDisputesQueryValidator : AbstractValidator<ListDisputesQuery>
{
    private static readonly string[] Statuses = ["live", "open", "underreview", "resolved", "withdrawn", "closed", "all"];

    public ListDisputesQueryValidator() =>
        RuleFor(query => query.Status)
            .Must(status => status is null || Statuses.Contains(status.ToLowerInvariant()))
            .WithMessage("Unknown dispute status filter.");
}

public sealed class ResolveDisputeCommandValidator : AbstractValidator<ResolveDisputeCommand>
{
    public ResolveDisputeCommandValidator()
    {
        RuleFor(command => command.RefundToCustomer).GreaterThanOrEqualTo(0m);
        RuleFor(command => command.RetainedByPlatform).GreaterThanOrEqualTo(0m);
        RuleFor(command => command.TransferredToDealer).GreaterThanOrEqualTo(0m);
        RuleFor(command => command.DealerCharge).GreaterThanOrEqualTo(0m).When(command => command.DealerCharge.HasValue);
        RuleFor(command => command.Note).NotEmpty().MaximumLength(2000);
    }
}

/// <summary>Resolve's rules, but for the note, which a preview may not have yet.</summary>
public sealed class PreviewDisputeResolutionQueryValidator : AbstractValidator<PreviewDisputeResolutionQuery>
{
    public PreviewDisputeResolutionQueryValidator()
    {
        RuleFor(query => query.RefundToCustomer).GreaterThanOrEqualTo(0m);
        RuleFor(query => query.RetainedByPlatform).GreaterThanOrEqualTo(0m);
        RuleFor(query => query.TransferredToDealer).GreaterThanOrEqualTo(0m);
        RuleFor(query => query.DealerCharge).GreaterThanOrEqualTo(0m).When(query => query.DealerCharge.HasValue);
        RuleFor(query => query.Note).MaximumLength(2000);
    }
}

public sealed class AdminDisputeHandlers(
    IDisputeTicketRepository tickets,
    IBookingRepository bookings,
    IPaymentRepository payments,
    IDisputeAdminReader reader,
    DisputeViewComposer composer,
    DisputeAuditor auditor,
    DealerTeamNotifier team,
    IDealerRepository dealers,
    IPayablesSettings payables,
    ICurrentActor actor,
    IClock clock,
    IUnitOfWork unitOfWork) :
    IRequestHandler<ListDisputesQuery, Result<PagedResult<DisputeListItem>, Error>>,
    IRequestHandler<GetDisputeQueueCountsQuery, Result<DisputeQueueCounts, Error>>,
    IRequestHandler<GetDisputeForReviewQuery, Result<DisputeDto, Error>>,
    IRequestHandler<AssignDisputeCommand, Result<DisputeDto, Error>>,
    IRequestHandler<ResolveDisputeCommand, Result<DisputeDto, Error>>,
    IRequestHandler<PreviewDisputeResolutionQuery, Result<ResolutionPreviewDto, Error>>
{
    public async Task<Result<PagedResult<DisputeListItem>, Error>> Handle(
        ListDisputesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await reader.ListAsync(
            new DisputeListFilter(request.Status, request.OverdueOnly),
            PageRequest.From(request.Page, request.PageSize),
            clock.UtcNow,
            cancellationToken);
    }

    public async Task<Result<DisputeQueueCounts, Error>> Handle(
        GetDisputeQueueCountsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await reader.CountsAsync(
            new DisputeListFilter(request.Status, request.OverdueOnly),
            clock.UtcNow,
            cancellationToken);
    }

    public async Task<Result<DisputeDto, Error>> Handle(GetDisputeForReviewQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ticket = await tickets.GetByIdAsync(request.TicketId, cancellationToken);
        if (ticket is null)
            return DisputeErrors.NotFound;

        return await composer.ComposeAsync(ticket, BookingParty.Admin, cancellationToken);
    }

    public async Task<Result<DisputeDto, Error>> Handle(AssignDisputeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ticket = await tickets.GetByIdAsync(request.TicketId, cancellationToken);
        if (ticket is null)
            return DisputeErrors.NotFound;
        var booking = await bookings.GetByIdAsync(ticket.BookingId, cancellationToken);
        if (booking is null)
            return DisputeErrors.BookingMissing;

        var previous = ticket.Status.Name;
        // Assignment is workflow, not a gate: whoever resolves is recorded then, and another admin
        // may still decide a ticket a colleague picked up.
        var assigned = ticket.AssignToAdmin(actor.UserId!.Value);
        if (assigned.IsFailure)
            return assigned.Error;

        auditor.Record(ticket, booking, AuditAction.DisputeAssigned, previous, ticket.Status.Name);
        await TellCustomerAsync(ticket, booking);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await composer.ComposeAsync(ticket, booking, BookingParty.Admin, cancellationToken);
    }

    public async Task<Result<DisputeDto, Error>> Handle(ResolveDisputeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var drafted = await DraftAsync(
            request.TicketId,
            request.RefundToCustomer,
            request.RetainedByPlatform,
            request.TransferredToDealer,
            request.DealerCharge,
            cancellationToken);
        if (drafted.IsFailure)
            return drafted.Error;
        var (ticket, booking, basis, disposition, dealerCharge, _) = drafted.Value;
        var now = clock.UtcNow;

        // Attributed to the signed-in admin, never to an id in the request body. Create states the office-charge
        // rule again, so the aggregate keeps its own invariant whatever called it.
        var resolution = DisputeResolution.Create(
            disposition,
            dealerCharge,
            booking.Penalty,
            request.Note,
            actor.UserId!.Value,
            now,
            basis.ChargedToDealerEarlier);
        if (resolution.IsFailure)
            return resolution.Error;

        var previous = ticket.Status.Name;
        var resolved = ticket.Resolve(resolution.Value);
        if (resolved.IsFailure)
            return resolved.Error;

        // A returned booking completes with the decision; one already over stays as it is, and only a
        // booking that really moved is announced as completed (Wave 3, C5).
        var wasReturned = booking.Status == BookingStatus.Returned;
        var closed = BookingDisputeSettlement.CloseAfterDispute(booking, actor.UserId!.Value, now);
        if (closed.IsFailure)
            return closed.Error;
        var completed = wasReturned && booking.Status == BookingStatus.Completed;

        // The disposition is a MONEY INSTRUCTION, and this is where the customer's leg of it stops
        // being a number on a screen. Recorded in this same save, so a resolution and the refund it
        // ordered can never come apart; SENT later by the payment sweep, because reaching a provider
        // is a network call that fails exactly when it matters most.
        //
        // The other two legs -- what the platform keeps and what goes to the dealer -- have no rail
        // and are settled by hand. That is the standing gap the architecture doc records, not
        // something this handler can close.
        var refunded = await RecordCustomerRefundAsync(booking.Id, ticket.Id, disposition.RefundToCustomer, now, cancellationToken);
        if (refunded.IsFailure)
            return refunded.Error;

        auditor.Record(
            ticket,
            booking,
            AuditAction.DisputeResolved,
            previous,
            DisputeAuditor.Describe(resolution.Value),
            resolution.Value.Note);
        await TellCustomerAsync(ticket, booking);
        await TellOfficeOfDecisionAsync(ticket, booking, completed, now, cancellationToken);

        // ONE SaveChangesAsync, deliberately not IUnitOfWork.ExecuteInTransactionAsync despite the
        // architecture rule for multi-aggregate writes. Both aggregates and the audit entry are
        // tracked by the same context, so a single save is already one database transaction. An
        // explicit outer transaction would make UnitOfWork dispatch domain events (BookingCompleted)
        // BEFORE commit, which is exactly what the post-commit rule exists to prevent.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await composer.ComposeAsync(ticket, booking, BookingParty.Admin, cancellationToken);
    }

    /// <summary>
    /// What a decision would be, after every check resolving makes and before anything changes (Wave 2 C1). Resolve
    /// is this plus its unchanged mutation steps; the preview is this plus the office's money. One drafter, so the two
    /// cannot refuse different things.
    /// </summary>
    private async Task<Result<ResolutionDraft, Error>> DraftAsync(
        Id ticketId,
        decimal refundToCustomer,
        decimal retainedByPlatform,
        decimal transferredToDealer,
        decimal? dealerCharge,
        CancellationToken cancellationToken)
    {
        var ticket = await tickets.GetByIdAsync(ticketId, cancellationToken);
        if (ticket is null)
            return DisputeErrors.NotFound;
        // Asked first, as DisputeTicket.Resolve asks again: a closed ticket is refused for being closed, not for
        // amounts measured against a basis it no longer has.
        if (ticket.Status == DisputeStatus.Resolved)
            return DisputeErrors.AlreadyResolved;
        if (ticket.Status == DisputeStatus.Withdrawn)
            return DisputeErrors.AlreadyWithdrawn;

        // The booking is loaded, not summarised: its frozen deposit is the basis of the split, its
        // assessed penalty is the range a dealer charge must fall inside, and it is about to be
        // closed. A ticket whose booking will not load is a data-integrity failure and stops here.
        var booking = await bookings.GetByIdAsync(ticket.BookingId, cancellationToken);
        if (booking is null)
            return DisputeErrors.BookingMissing;

        // A deposit the window already released is not held any more (Phase 3). Asked only while the
        // booking holds one at all.
        var released =
            !BookingDisputeSettlement.DepositHeldFor(booking).IsZero &&
            booking.DepositPaymentId is { } depositPaymentId &&
            (await payments.GetByIdAsync(depositPaymentId, cancellationToken))?.RefundFor(RefundReason.DisputeWindowClosed) is not null;

        // What THIS ticket may split: the deposit less what the booking's earlier disputes decided
        // (owner, 2026-09-26; item 169). Every resolution still decides the whole of its basis, so a
        // ticket opened after one was resolved splits nothing and can close only with a note.
        var resolved = await tickets.ListResolvedForBookingAsync(booking.Id, cancellationToken);
        var basis = DisputedDeposit.For(ticket, booking, released, resolved);
        if (basis.IsFailure)
            return basis.Error;
        var held = basis.Value.Basis;
        var currency = held.CurrencyCode;

        // Before any Money is built: Money rounds, and a split typed with a fourth decimal would otherwise
        // be decided in a form the administrator never entered (E2E F36).
        if (!Money.FitsMinorUnits(refundToCustomer) ||
            !Money.FitsMinorUnits(retainedByPlatform) ||
            !Money.FitsMinorUnits(transferredToDealer) ||
            (dealerCharge is { } typed && !Money.FitsMinorUnits(typed)))
        {
            return DisputeErrors.AmountPrecision;
        }

        var disposition = DepositDisposition.Create(
            held,
            Money.Create(refundToCustomer, currency),
            Money.Create(retainedByPlatform, currency),
            Money.Create(transferredToDealer, currency));
        if (disposition.IsFailure)
            return disposition.Error;

        // The office charge is bounded by the booking's range across every dispute on it, not per ticket.
        var charge = dealerCharge is { } amount ? Money.Create(amount, currency) : null;
        var fits = DisputeResolution.CheckDealerCharge(charge, booking.Penalty, basis.Value.ChargedToDealerEarlier);
        if (fits.IsFailure)
            return fits.Error;

        var earlier = resolved
            .Where(other => other.Id != ticket.Id && other.BookingId == booking.Id && other.Resolution is not null)
            .ToList();
        return new ResolutionDraft(ticket, booking, basis.Value, disposition.Value, charge, earlier);
    }

    public async Task<Result<ResolutionPreviewDto, Error>> Handle(
        PreviewDisputeResolutionQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var drafted = await DraftAsync(
            request.TicketId,
            request.RefundToCustomer,
            request.RetainedByPlatform,
            request.TransferredToDealer,
            request.DealerCharge,
            cancellationToken);
        if (drafted.IsFailure)
            return drafted.Error;
        var (ticket, booking, basis, disposition, dealerCharge, earlier) = drafted.Value;
        var now = clock.UtcNow;

        var after = BookingDisputeSettlement.AfterResolution(booking, now);
        if (after.IsFailure)
            return after.Error;

        // This decision among the booking's others, as the ledger will read them all once it is recorded.
        var currency = booking.Pricing.CurrencyCode;
        var decided = earlier
            .Select(DecidedDispute.Of)
            .Append(new DecidedDispute(ticket.Id, ticket.OpenedAt, disposition.TransferredToDealer, dealerCharge))
            .ToList();
        var office = BookingFinancialsCalculator.OfficeAtFinality(booking, after.Value.Status, after.Value.FinalAt, decided, currency);

        // The ledger's own consistency check, on the records as they stand (advisor's review of Wave 2): a booking whose
        // records contradict one another is HELD for review instead of recorded, whatever this decision says, and the
        // preview must not promise otherwise. Read with this ticket live, as the ledger would read the booking now. The
        // decision cannot add a contradiction of its own: its shares balance by construction, and its refund has the
        // ticket for a cause.
        var ledgerIssues = BookingFinancialsCalculator.Calculate(
                booking,
                await payments.ListForBookingAsync(booking.Id, cancellationToken),
                earlier,
                hasLiveDispute: true,
                now)
            .Issues;

        return ResolutionPreview(booking, ticket, disposition, office, earlier, after.Value, ledgerIssues, now, currency);
    }

    private ResolutionPreviewDto ResolutionPreview(
        Booking booking,
        DisputeTicket ticket,
        DepositDisposition disposition,
        OfficePosition office,
        List<DisputeTicket> earlier,
        BookingAfterDispute after,
        IReadOnlyList<string> ledgerIssues,
        DateTimeOffset now,
        string currency)
    {
        MoneyDto Of(decimal amount) => new(Money.AtScale(amount), currency);

        // The ledger takes a booking once it is final and the margin has passed (advisor's review of Wave 2). A decision
        // made after a cancellation's window closed can find that moment already behind it: the next pass records it
        // then, and the floor is now, not now plus the margin.
        var ready = after.FinalAt.Add(payables.FinalityMargin);
        var atNextPass = ready <= now;
        var notBefore = atNextPass ? now : ready;
        var earlierDecisions = earlier.Count == 0
            ? null
            : new ResolutionPreviewEarlierDto(
                earlier.Count,
                Of(earlier.Sum(other => other.Resolution!.Deposit.RefundToCustomer.Amount)),
                Of(earlier.Sum(other => other.Resolution!.Deposit.RetainedByPlatform.Amount)),
                Of(earlier.Sum(other => other.Resolution!.Deposit.TransferredToDealer.Amount)),
                Of(earlier.Sum(other => other.Resolution!.DealerCharge?.Amount ?? 0m)));

        return new ResolutionPreviewDto(
            after.Status.Name,
            office.State,
            office.Outcome?.Name,
            office.Lines.Select(line => new PayableLineDto(line.Kind.Name, Of(line.Amount), line.SourceId?.Value)).ToList(),
            new ResolutionPreviewCustomerDto(MoneyDto.From(disposition.RefundToCustomer)),
            new ResolutionPreviewPlatformDto(MoneyDto.From(disposition.RetainedByPlatform), MoneyDto.From(office.Commission)),
            new ResolutionPreviewOfficeDto(
                MoneyDto.From(disposition.TransferredToDealer),
                MoneyDto.From(office.OfficeMoney),
                MoneyDto.From(booking.Pricing.CommissionAmount),
                MoneyDto.From(office.Commission),
                MoneyDto.From(office.Charges),
                Of(office.Net)),
            earlierDecisions,
            notBefore,
            after.FurtherDisputesUntil,
            BookingFinancials.CalculatorVersion,
            ledgerIssues,
            atNextPass);
    }

    /// <summary>A decision that passed every check, and the facts it was checked against.</summary>
    private sealed record ResolutionDraft(
        DisputeTicket Ticket,
        Booking Booking,
        DisputeBasis Basis,
        DepositDisposition Disposition,
        Money? DealerCharge,
        List<DisputeTicket> Earlier);

    /// <summary>
    /// Records what the resolution returns to the customer, against the payment that actually took it.
    /// </summary>
    /// <remarks>
    /// Silent in three cases, each of which is correct rather than a gap:
    /// nothing to refund; a booking whose deposit was never paid (its disposition is all zeros, which
    /// <c>DepositDisposition.Create</c> already insists on); and a deposit taken before Payments
    /// existed, which has no payment row to refund against and has to be settled by hand.
    /// </remarks>
    private async Task<UnitResult<Error>> RecordCustomerRefundAsync(
        Id bookingId,
        Id ticketId,
        Money refundToCustomer,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (refundToCustomer.IsZero)
            return UnitResult.Success<Error>();

        var payment = await payments.GetAppliedForBookingAsync(bookingId, cancellationToken);
        if (payment is null)
            return UnitResult.Success<Error>();

        // A FRESH Money: the disposition's instance is owned by the ticket, and EF must not see one
        // value object tracked under two aggregates.
        var amount = Money.Create(refundToCustomer.Amount, refundToCustomer.CurrencyCode);
        var requested = payment.RequestRefund(amount, ticketId, now);
        return requested.IsSuccess ? UnitResult.Success<Error>() : UnitResult.Failure(requested.Error);
    }

    /// <summary>
    /// The office's team hears that Khadra decided the dispute, in the console and by email, and that the booking
    /// completed when the decision completed it (Fix & Polish Wave 3, C5). Staged, not saved; the dealership is read,
    /// never written.
    /// </summary>
    private async Task TellOfficeOfDecisionAsync(
        DisputeTicket ticket,
        Domain.Bookings.Booking booking,
        bool completed,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (await dealers.GetByIdAsync(booking.DealerId, cancellationToken) is not { } dealer)
            return;

        await team.NotifyTeamFromPlatformAsync(dealer, NotificationKind.DisputeResolved, now, ticket.Id, booking.Reference.Value);
        if (completed)
            await team.NotifyTeamFromPlatformAsync(dealer, NotificationKind.BookingCompleted, now, booking.Id, booking.Reference.Value);
    }

    /// <summary>The booking's customer hears that the platform moved their dispute on. Staged, not saved.</summary>
    /// <remarks>
    /// The subject is the TICKET, so tapping the push opens the dispute. Named "Khadra": the platform
    /// decided this, not the gallery. Nothing about the outcome travels in the push — the app shows it.
    /// </remarks>
    private Task TellCustomerAsync(DisputeTicket ticket, Domain.Bookings.Booking booking) =>
        team.NotifyCustomerFromPlatformAsync(
            booking.CustomerId,
            NotificationKind.YourDisputeUpdated,
            clock.UtcNow,
            ticket.Id,
            booking.Reference.Value);
}
