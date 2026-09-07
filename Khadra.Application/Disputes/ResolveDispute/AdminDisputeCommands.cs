using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Bookings;
using Khadra.Application.Common;
using Khadra.Application.Disputes.Dtos;
using Khadra.Application.Disputes.ReadModels;
using Khadra.Domain.Auditing;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Khadra.Domain.Disputes.Repositories;
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

public sealed class AdminDisputeHandlers(
    IDisputeTicketRepository tickets,
    IBookingRepository bookings,
    IDisputeAdminReader reader,
    DisputeViewComposer composer,
    DisputeAuditor auditor,
    ICurrentActor actor,
    IClock clock,
    IUnitOfWork unitOfWork) :
    IRequestHandler<ListDisputesQuery, Result<PagedResult<DisputeListItem>, Error>>,
    IRequestHandler<GetDisputeQueueCountsQuery, Result<DisputeQueueCounts, Error>>,
    IRequestHandler<GetDisputeForReviewQuery, Result<DisputeDto, Error>>,
    IRequestHandler<AssignDisputeCommand, Result<DisputeDto, Error>>,
    IRequestHandler<ResolveDisputeCommand, Result<DisputeDto, Error>>
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

        return await composer.ComposeAsync(ticket, cancellationToken);
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
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await composer.ComposeAsync(ticket, booking, cancellationToken);
    }

    public async Task<Result<DisputeDto, Error>> Handle(ResolveDisputeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ticket = await tickets.GetByIdAsync(request.TicketId, cancellationToken);
        if (ticket is null)
            return DisputeErrors.NotFound;

        // The booking is loaded, not summarised: its frozen deposit is the basis of the split, its
        // assessed penalty is the range a dealer charge must fall inside, and it is about to be
        // closed. A ticket whose booking will not load is a data-integrity failure and stops here.
        var booking = await bookings.GetByIdAsync(ticket.BookingId, cancellationToken);
        if (booking is null)
            return DisputeErrors.BookingMissing;

        var now = clock.UtcNow;
        var held = BookingDisputeSettlement.DepositHeldFor(booking);
        var currency = held.CurrencyCode;

        var disposition = DepositDisposition.Create(
            held,
            Money.Create(request.RefundToCustomer, currency),
            Money.Create(request.RetainedByPlatform, currency),
            Money.Create(request.TransferredToDealer, currency));
        if (disposition.IsFailure)
            return disposition.Error;

        // Attributed to the signed-in admin, never to an id in the request body.
        var resolution = DisputeResolution.Create(
            disposition.Value,
            request.DealerCharge is { } charge ? Money.Create(charge, currency) : null,
            booking.Penalty,
            request.Note,
            actor.UserId!.Value,
            now);
        if (resolution.IsFailure)
            return resolution.Error;

        var previous = ticket.Status.Name;
        var resolved = ticket.Resolve(resolution.Value);
        if (resolved.IsFailure)
            return resolved.Error;

        var closed = BookingDisputeSettlement.CloseAfterDispute(booking, actor.UserId!.Value, now);
        if (closed.IsFailure)
            return closed.Error;

        auditor.Record(
            ticket,
            booking,
            AuditAction.DisputeResolved,
            previous,
            DisputeAuditor.Describe(resolution.Value),
            resolution.Value.Note);

        // ONE SaveChangesAsync, deliberately not IUnitOfWork.ExecuteInTransactionAsync despite the
        // architecture rule for multi-aggregate writes. Both aggregates and the audit entry are
        // tracked by the same context, so a single save is already one database transaction. An
        // explicit outer transaction would make UnitOfWork dispatch domain events (BookingCompleted)
        // BEFORE commit, which is exactly what the post-commit rule exists to prevent.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await composer.ComposeAsync(ticket, booking, cancellationToken);
    }
}
