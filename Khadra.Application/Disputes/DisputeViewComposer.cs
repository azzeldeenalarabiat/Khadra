using CSharpFunctionalExtensions;
using Khadra.Application.Bookings;
using Khadra.Application.Bookings.Dtos;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Application.Common.Dtos;
using Khadra.Application.Common.Ports;
using Khadra.Application.Disputes.Dtos;
using Khadra.Application.Disputes.ReadModels;
using Khadra.Application.Payables.Dtos;
using Khadra.Application.Payments.Financials;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Khadra.Domain.Disputes.Repositories;
using Khadra.Domain.Payables.Repositories;
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
/// stored URL would be a credential sitting in a database. Each is bound to the signed-in person the view
/// is composed for, so it opens in their session and nobody else's (pre-launch item 14).
/// </summary>
public sealed partial class DisputeViewComposer(
    IBookingRepository bookings,
    IBookingReader bookingReader,
    IDisputeAdminReader names,
    IDocumentLinkSigner signer,
    ICurrentActor actor,
    IDisputeTicketRepository tickets,
    IAdminDashboardSettings dashboard,
    IOfficePayableRepository payables,
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
        // Asked only when there is evidence to link. Every route that composes a dispute is behind authentication, so
        // somebody is signed in; nobody would be a programming error, not a link to hand out.
        Id Reader() => actor.UserId ?? throw new InvalidOperationException("An evidence link is minted for the signed-in person who will open it.");
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
                        var link = signer.Sign(key, Reader(), now);
                        return new EvidenceLinkDto(Path.GetFileName(key), link.Url, link.ExpiresAt);
                    })
                    .ToList()))
            .ToList();

        // Read once, for the basis and for the office's outcome alike. Sequential, never concurrent: every reader here
        // shares the scoped DbContext.
        var resolvedTickets = await tickets.ListResolvedForBookingAsync(booking.Id, cancellationToken);
        var basis = Basis(ticket, booking, context, resolvedTickets);
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

        var view = new DisputeDto(
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
            viewer == BookingParty.Admin ? DisputeSlaStates.For(ticket, dashboard.SlaWarningThreshold, now) : null,
            // The office's copy only: what the decision comes to for its money (Wave 2 C1).
            viewer == BookingParty.Dealer ? await ExpectedOutcomeAsync(ticket, booking, resolvedTickets, now, cancellationToken) : null);
        // The customer's copy names the platform and the office, never their people (D5 A; owner, 2026-10-06, Q4).
        return viewer == BookingParty.Customer ? view.ForCustomer(context.DealerName) : view;
    }

    /// <summary>
    /// What a decided dispute comes to for the office (Wave 2 C1; E2E F37): the recorded payable once the ledger has
    /// one, and before that the projection of the ledger's own office function. The office's dispute page and its
    /// Payouts page therefore cannot disagree. Null while the ticket is undecided, and for a booking nothing was paid
    /// online for, which never gets a payable.
    /// </summary>
    private async Task<OfficeExpectedOutcomeDto?> ExpectedOutcomeAsync(
        DisputeTicket ticket,
        Booking booking,
        IReadOnlyList<DisputeTicket> resolved,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (ticket.Resolution is null || booking.DepositPaymentId is null)
            return null;

        var recorded = await payables.GetByBookingAsync(booking.Id, cancellationToken);
        if (recorded is not null)
        {
            MoneyDto Recorded(decimal amount) => new(Money.AtScale(amount), recorded.Currency);
            return new OfficeExpectedOutcomeDto(
                OfficeOutcomeSources.Recorded,
                recorded.Id.Value,
                recorded.Outcome.Name,
                Recorded(recorded.OfficeMoney),
                Recorded(recorded.Commission),
                Recorded(recorded.OfficeCharges),
                Recorded(recorded.Net),
                recorded.Lines.Select(line => new PayableLineDto(line.Kind.Name, Recorded(line.Amount), line.SourceId?.Value)).ToList(),
                recorded.FinalAt,
                null);
        }

        // A decision completes a returned booking, so a decided ticket's booking is completed, cancelled or a no-show.
        var finalAt =
            booking.Status == BookingStatus.Completed ? booking.FinishedAt
            : booking.Status == BookingStatus.Cancelled || booking.Status == BookingStatus.NoShow ? booking.DisputeWindowEndsAt
            : null;
        if (finalAt is not { } final)
            return null;

        var currency = booking.Pricing.CurrencyCode;
        var decided = resolved
            .Where(other => other.Id != ticket.Id && other.BookingId == booking.Id && other.Resolution is not null)
            .Append(ticket)
            .Select(DecidedDispute.Of)
            .ToList();
        var office = BookingFinancialsCalculator.OfficeAtFinality(booking, booking.Status, final, decided, currency);
        if (office.Outcome is null)
            return null;

        // This ticket is decided, so a live ticket on the booking is another one, and its decision will change these
        // figures however long ago the window closed: the ledger waits for it too (advisor's review of Wave 2).
        var anotherOpen = await tickets.HasLiveTicketAsync(booking.Id, cancellationToken);

        MoneyDto Projected(decimal amount) => new(Money.AtScale(amount), currency);
        return new OfficeExpectedOutcomeDto(
            OfficeOutcomeSources.Projected,
            null,
            office.Outcome.Name,
            Projected(office.OfficeMoney.Amount),
            Projected(office.Commission.Amount),
            Projected(office.Charges.Amount),
            Projected(office.Net),
            office.Lines.Select(line => new PayableLineDto(line.Kind.Name, Projected(line.Amount), line.SourceId?.Value)).ToList(),
            final,
            // Another dispute may still be opened on a cancellation or a no-show until its window closes.
            booking.Status != BookingStatus.Completed && now < final ? final : null,
            anotherOpen);
    }

    /// <summary>
    /// The three deposit figures a ticket shows, all from <see cref="DisputedDeposit"/>: a resolved
    /// ticket keeps the basis it was decided against, never a recomputed one.
    /// </summary>
    private (MoneyDto Held, MoneyDto? OnBooking, MoneyDto? DecidedEarlier, MoneyDto? ChargedEarlier) Basis(
        DisputeTicket ticket,
        Booking booking,
        BookingContext context,
        IReadOnlyList<DisputeTicket> resolved)
    {
        var released = (context.Refunds ?? []).Any(refund => refund.Reason == RefundReason.DisputeWindowClosed.Name);
        var basis = DisputedDeposit.For(ticket, booking, released, resolved);

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
