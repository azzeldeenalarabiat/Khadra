using CSharpFunctionalExtensions;
using Khadra.Application.Bookings;
using Khadra.Application.Bookings.Dtos;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Application.Common.Dtos;
using Khadra.Application.Common.Ports;
using Khadra.Application.Disputes.Dtos;
using Khadra.Application.Disputes.ReadModels;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Khadra.Domain.Disputes.Repositories;
using Khadra.Domain.Payments;
using Microsoft.Extensions.Logging;

namespace Khadra.Application.Disputes;

/// <summary>
/// Turns a ticket into the view everyone shares -- the parties and the Admin see the same dispute,
/// because both sides' statements are visible to both sides by design (one ticket per booking). The
/// money is projected per reader: the rental office's copy of the booking and of the decision carries
/// only what is the office's (owner decision 3).
///
/// Evidence links are minted here, per request, and expire: spec 7 keeps evidence private, and a
/// stored URL would be a credential sitting in a database.
/// </summary>
public sealed partial class DisputeViewComposer(
    IBookingRepository bookings,
    IBookingReader bookingReader,
    IDisputeAdminReader names,
    IDocumentLinkSigner signer,
    IDisputeTicketRepository tickets,
    IAdminDashboardSettings dashboard,
    IClock clock,
    ILogger<DisputeViewComposer> logger)
{
    private const string ClosedAccountName = "Account closed";

    public async Task<Result<DisputeDto, Error>> ComposeAsync(
        DisputeTicket ticket,
        BookingParty viewer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        var booking = await bookings.GetByIdAsync(ticket.BookingId, cancellationToken);
        if (booking is null)
            return DisputeErrors.BookingMissing;

        return await ComposeAsync(ticket, booking, viewer, cancellationToken);
    }

    /// <param name="viewer">
    /// The party the view is for, always named: a path that forgot it must not get the administrator's
    /// copy by default. A customer's copy carries no commission; an office's carries no refund, no fee,
    /// and of a decision only the basis, the office's own share and any charge to it (owner decision 3).
    /// </param>
    public async Task<DisputeDto> ComposeAsync(
        DisputeTicket ticket,
        Booking booking,
        BookingParty viewer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(booking);
        ArgumentNullException.ThrowIfNull(viewer);

        var now = clock.UtcNow;
        var context = await bookingReader.ContextAsync(booking.Id, cancellationToken);

        var userIds = ticket.Statements.Select(statement => statement.AuthorUserId)
            .Append(ticket.OpenedByUserId)
            .Concat(ticket.AssignedAdminId is { } assigned ? [assigned] : [])
            .Concat(ticket.Resolution is { } resolution ? [resolution.ResolvedByAdminId] : [])
            .Distinct()
            .ToList();
        var lookup = await names.NamesAsync(userIds, cancellationToken);
        // The stand-in is kept ONLY for shipped customer apps, which print a name as it arrives. Each
        // name travels with its flag, and a client that words the case reads the flag instead.
        string NameOf(Id id) => lookup.TryGetValue(id.Value, out var name) ? name : ClosedAccountName;
        bool Closed(Id id) => !lookup.ContainsKey(id.Value);

        var statements = ticket.Statements
            .OrderBy(statement => statement.CreatedAt)
            .Select(statement => new DisputeStatementDto(
                statement.Id.Value,
                statement.Party.Name,
                statement.AuthorUserId.Value,
                NameOf(statement.AuthorUserId),
                Closed(statement.AuthorUserId),
                statement.Body,
                statement.CreatedAt,
                statement.EvidenceStorageKeys
                    .Select(key =>
                    {
                        var link = signer.Sign(key, now);
                        return new EvidenceLinkDto(Path.GetFileName(key), link.Url, link.ExpiresAt);
                    })
                    .ToList()))
            .ToList();

        var basis = await BasisAsync(ticket, booking, context, cancellationToken);
        var decision = ticket.Resolution is { } resolved
            ? DisputeResolutionDto.From(resolved, NameOf(resolved.ResolvedByAdminId), Closed(resolved.ResolvedByAdminId))
            : null;
        var copy = BookingDto.From(booking, context, now);
        // Each reader's copy, named explicitly, the same shape as BookingFinancialsDto.For: anything
        // else is a programming error, never a quiet fall-through to the administrator's view.
        (copy, decision) = viewer == BookingParty.Customer ? (copy.ForCustomer(), decision)
            : viewer == BookingParty.Dealer ? (copy.ForDealer(), decision?.ForDealer())
            : viewer == BookingParty.Admin ? (copy, decision)
            : throw new ArgumentOutOfRangeException(nameof(viewer), viewer.Name, "A dispute is read by the customer, the office or an administrator.");

        return new DisputeDto(
            ticket.Id.Value,
            ticket.BookingId.Value,
            ticket.Status.Name,
            ticket.Status.IsLive,
            ticket.OpenedByParty.Name,
            ticket.OpenedByUserId.Value,
            NameOf(ticket.OpenedByUserId),
            Closed(ticket.OpenedByUserId),
            ticket.Reason,
            ticket.OpenedAt,
            ticket.SlaDeadline,
            ticket.IsBreachingSla(now),
            ticket.AssignedAdminId?.Value,
            ticket.AssignedAdminId is { } admin ? NameOf(admin) : null,
            ticket.AssignedAdminId is { } holder && Closed(holder),
            ticket.ClosedAt,
            statements,
            decision,
            basis.Held,
            copy,
            basis.OnBooking,
            basis.DecidedEarlier,
            // Administrators only (owner decision 3): a customer is never shown what the office was charged,
            // and the SLA panel is the administrator's alone. The parties' copies carry null.
            viewer == BookingParty.Admin ? basis.ChargedEarlier : null,
            viewer == BookingParty.Admin ? DisputeSlaStates.For(ticket, dashboard.SlaWarningThreshold, now) : null);
    }

    /// <summary>
    /// The three deposit figures a ticket shows, all from <see cref="DisputedDeposit"/>: a resolved
    /// ticket keeps the basis it was decided against, never a recomputed one.
    /// </summary>
    private async Task<(MoneyDto Held, MoneyDto? OnBooking, MoneyDto? DecidedEarlier, MoneyDto? ChargedEarlier)> BasisAsync(
        DisputeTicket ticket,
        Booking booking,
        BookingContext context,
        CancellationToken cancellationToken)
    {
        var released = (context.Refunds ?? []).Any(refund => refund.Reason == RefundReason.DisputeWindowClosed.Name);
        var basis = DisputedDeposit.For(
            ticket,
            booking,
            released,
            await tickets.ListResolvedForBookingAsync(booking.Id, cancellationToken));

        if (basis.IsFailure)
        {
            // Never a 500 on a read, and never a figure the server knows is wrong: the handler refuses
            // to split, this says why somebody must look, and the two explaining figures are left out
            // (every client reads their absence as "nothing to explain"). depositHeld stays zero for a
            // live ticket — non-nullable in the contract, and the only split the handler would accept.
            LogOverAllocated(logger, ticket.Id.Value, booking.Id.Value);
            var nothing = MoneyDto.From(Money.ZeroIn(booking.Pricing.CurrencyCode));
            return (ticket.Resolution is { } decided ? MoneyDto.From(decided.Deposit.DepositHeld) : nothing, null, null, null);
        }

        var held = ticket.Resolution is { } resolution
            ? MoneyDto.From(resolution.Deposit.DepositHeld)
            : MoneyDto.From(basis.Value.Basis);
        return (
            held,
            MoneyDto.From(basis.Value.DepositOnBooking),
            MoneyDto.From(basis.Value.DecidedByEarlierTickets),
            MoneyDto.From(basis.Value.ChargedToDealerEarlier));
    }

    [LoggerMessage(
        2400,
        LogLevel.Error,
        "Dispute {TicketId}: earlier decisions on booking {BookingId} allocated more than its deposit. Nothing can be split until a human corrects it.")]
    private static partial void LogOverAllocated(ILogger logger, Guid ticketId, Guid bookingId);
}
