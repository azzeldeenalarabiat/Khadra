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

namespace Khadra.Application.Disputes;

/// <summary>
/// Turns a ticket into the view everyone shares -- the parties and the Admin see the same dispute,
/// because both sides' statements are visible to both sides by design (one ticket per booking).
///
/// Evidence links are minted here, per request, and expire: spec 7 keeps evidence private, and a
/// stored URL would be a credential sitting in a database.
/// </summary>
public sealed class DisputeViewComposer(
    IBookingRepository bookings,
    IBookingReader bookingReader,
    IDisputeAdminReader names,
    IDocumentLinkSigner signer,
    IClock clock)
{
    public async Task<Result<DisputeDto, Error>> ComposeAsync(DisputeTicket ticket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        var booking = await bookings.GetByIdAsync(ticket.BookingId, cancellationToken);
        if (booking is null)
            return DisputeErrors.BookingMissing;

        return await ComposeAsync(ticket, booking, cancellationToken);
    }

    public async Task<DisputeDto> ComposeAsync(DisputeTicket ticket, Booking booking, CancellationToken cancellationToken)
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
        string NameOf(Id id) => lookup.TryGetValue(id.Value, out var name) ? name : "Account closed";

        var statements = ticket.Statements
            .OrderBy(statement => statement.CreatedAt)
            .Select(statement => new DisputeStatementDto(
                statement.Id.Value,
                statement.Party.Name,
                statement.AuthorUserId.Value,
                NameOf(statement.AuthorUserId),
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

        return new DisputeDto(
            ticket.Id.Value,
            ticket.BookingId.Value,
            ticket.Status.Name,
            ticket.Status.IsLive,
            ticket.OpenedByParty.Name,
            ticket.OpenedByUserId.Value,
            NameOf(ticket.OpenedByUserId),
            ticket.Reason,
            ticket.OpenedAt,
            ticket.SlaDeadline,
            ticket.IsBreachingSla(now),
            ticket.AssignedAdminId?.Value,
            ticket.AssignedAdminId is { } admin ? NameOf(admin) : null,
            ticket.ClosedAt,
            statements,
            ticket.Resolution is { } resolved
                ? DisputeResolutionDto.From(resolved, NameOf(resolved.ResolvedByAdminId))
                : null,
            MoneyDto.From(BookingDisputeSettlement.DepositHeldFor(booking)),
            BookingDto.From(booking, context, now));
    }
}
