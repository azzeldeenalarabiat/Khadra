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
/// because both sides' statements are visible to both sides by design (one ticket per booking).
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
    IClock clock,
    ILogger<DisputeViewComposer> logger)
{
    private const string ClosedAccountName = "Account closed";

    public async Task<Result<DisputeDto, Error>> ComposeAsync(DisputeTicket ticket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        var booking = await bookings.GetByIdAsync(ticket.BookingId, cancellationToken);
        if (booking is null)
            return DisputeErrors.BookingMissing;

        return await ComposeAsync(ticket, booking, cancellationToken);
    }

    /// <param name="viewer">The party the view is for. A customer's copy carries no commission.</param>
    public async Task<DisputeDto> ComposeAsync(
        DisputeTicket ticket,
        Booking booking,
        CancellationToken cancellationToken,
        BookingParty? viewer = null)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(booking);

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
            ticket.Resolution is { } resolved
                ? DisputeResolutionDto.From(
                    resolved,
                    NameOf(resolved.ResolvedByAdminId),
                    Closed(resolved.ResolvedByAdminId))
                : null,
            basis.Held,
            viewer == BookingParty.Customer
                ? BookingDto.From(booking, context, now).ForCustomer()
                : BookingDto.From(booking, context, now),
            basis.OnBooking,
            basis.DecidedEarlier);
    }

    /// <summary>
    /// The three deposit figures a ticket shows, all from <see cref="DisputedDeposit"/>: a resolved
    /// ticket keeps the basis it was decided against, never a recomputed one.
    /// </summary>
    private async Task<(MoneyDto Held, MoneyDto? OnBooking, MoneyDto? DecidedEarlier)> BasisAsync(
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
            return (ticket.Resolution is { } decided ? MoneyDto.From(decided.Deposit.DepositHeld) : nothing, null, null);
        }

        var held = ticket.Resolution is { } resolution
            ? MoneyDto.From(resolution.Deposit.DepositHeld)
            : MoneyDto.From(basis.Value.Basis);
        return (held, MoneyDto.From(basis.Value.DepositOnBooking), MoneyDto.From(basis.Value.DecidedByEarlierTickets));
    }

    [LoggerMessage(
        2400,
        LogLevel.Error,
        "Dispute {TicketId}: earlier decisions on booking {BookingId} allocated more than its deposit. Nothing can be split until a human corrects it.")]
    private static partial void LogOverAllocated(ILogger logger, Guid ticketId, Guid bookingId);
}
