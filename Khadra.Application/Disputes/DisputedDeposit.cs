using CSharpFunctionalExtensions;
using Khadra.Application.Bookings;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;

namespace Khadra.Application.Disputes;

/// <summary>
/// What a dispute on a booking may still split, and what earlier disputes already decided (owner,
/// 2026-09-26; pre-launch item 169).
/// </summary>
/// <param name="DepositOnBooking">
/// The deposit the platform held for disputes before any resolution: the booking's frozen deposit, or
/// zero once its whole payment went back or its window released the deposit.
/// </param>
/// <param name="DecidedByEarlierTickets">What the booking's earlier RESOLVED disputes allocated.</param>
/// <param name="Basis">
/// What THIS dispute may split: the deposit less what earlier ones decided. Every resolution still
/// allocates the whole of its basis, so after one resolution the basis of the next is zero.
/// </param>
/// <param name="ChargedToDealerEarlier">What earlier resolutions charged the office, off the deposit rail.</param>
public sealed record DisputeBasis(
    Money DepositOnBooking,
    Money DecidedByEarlierTickets,
    Money Basis,
    Money ChargedToDealerEarlier);

/// <summary>
/// The one calculator of what a dispute may split. The resolve handler validates a decision against
/// it and the view composer shows it, so the form and the rule cannot disagree — and no client ever
/// subtracts.
/// </summary>
/// <remarks>
/// <para>
/// A second ticket on a booking is reachable only on a CANCELLED or NO-SHOW booking: resolving a
/// dispute on a returned booking completes it (<c>Booking.CloseAfterDisputeResolved</c>), and a
/// completed booking can no longer be disputed. Opening a second ticket stays allowed (the installed
/// app sends that request, and refusing it would break the contract); what it can split is what the
/// earlier ones left, which today is always zero, because every resolution decides the whole of its
/// basis.
/// </para>
/// <para>
/// "Earlier" is decided WITHOUT comparing timestamps (pre-launch item 171): a tie or a skew between two nodes'
/// clocks inside an open-resolve-open sequence could otherwise make neither ticket earlier, and offer both the whole
/// deposit. For a LIVE ticket every other resolved ticket is earlier — only one ticket per booking can be live, so
/// one can open only after the previous one closed. A RESOLVED ticket keeps the basis stored on its own decision,
/// and what earlier disputes decided is that basis subtracted from the deposit on the booking, whatever later
/// tickets do.
/// </para>
/// </remarks>
public static class DisputedDeposit
{
    /// <param name="depositReleased">
    /// Whether the booking's deposit already went back because its dispute window closed cleanly
    /// (a <c>DisputeWindowClosed</c> refund on its deposit payment).
    /// </param>
    /// <param name="resolvedTickets">The booking's resolved tickets; which of them count is decided above.</param>
    public static Result<DisputeBasis, Error> For(
        DisputeTicket ticket,
        Booking booking,
        bool depositReleased,
        IEnumerable<DisputeTicket> resolvedTickets)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(booking);
        ArgumentNullException.ThrowIfNull(resolvedTickets);

        var deposit = BookingDisputeSettlement.DepositHeldFor(booking, depositReleased);
        var others = resolvedTickets
            .Where(other => other.Id != ticket.Id && other.BookingId == ticket.BookingId && other.Resolution is not null)
            .ToList();

        return ticket.Resolution is { } own
            ? Decided(ticket, own, booking, deposit, others)
            : Live(booking, deposit, others);
    }

    /// <summary>A live ticket: every other resolved ticket of the booking decided before it.</summary>
    private static Result<DisputeBasis, Error> Live(Booking booking, Money deposit, List<DisputeTicket> earlier)
    {
        var currency = deposit.CurrencyCode;
        var decided = 0m;
        var charged = 0m;
        foreach (var other in earlier)
        {
            decided += SplitOf(other, booking, currency);
            charged += ChargeOf(other, booking, currency);
        }

        // More decided than was ever held is a data-integrity failure, never a figure to show or to
        // split: refused, not floored at zero.
        if (decided > deposit.Amount)
            return DisputeErrors.DepositOverAllocated;

        return new DisputeBasis(
            Money.Create(deposit.Amount, currency),
            Money.Create(decided, currency),
            Money.Create(deposit.Amount - decided, currency),
            Money.Create(charged, currency));
    }

    /// <summary>
    /// A resolved ticket: the basis its own decision stored, and what earlier disputes decided as that basis taken
    /// from the deposit on the booking. What they charged the office is shown only, never validated against, so it is
    /// read by resolution order.
    /// </summary>
    private static Result<DisputeBasis, Error> Decided(
        DisputeTicket ticket,
        DisputeResolution own,
        Booking booking,
        Money deposit,
        List<DisputeTicket> others)
    {
        var currency = deposit.CurrencyCode;
        var basis = own.Deposit.DepositHeld;
        if (!string.Equals(basis.CurrencyCode, currency, StringComparison.Ordinal))
            throw new DomainException($"Dispute {ticket.Id} split a deposit in another currency than booking {booking.Id}.");
        if (basis.Amount > deposit.Amount)
            return DisputeErrors.DepositOverAllocated;

        var charged = others
            .Where(other => other.Resolution!.ResolvedAt < own.ResolvedAt)
            .Sum(other => ChargeOf(other, booking, currency));

        return new DisputeBasis(
            Money.Create(deposit.Amount, currency),
            Money.Create(deposit.Amount - basis.Amount, currency),
            Money.Create(basis.Amount, currency),
            Money.Create(charged, currency));
    }

    private static decimal SplitOf(DisputeTicket ticket, Booking booking, string currency)
    {
        var split = ticket.Resolution!.Deposit;
        if (!string.Equals(split.DepositHeld.CurrencyCode, currency, StringComparison.Ordinal))
            throw new DomainException($"Dispute {ticket.Id} split a deposit in another currency than booking {booking.Id}.");
        return split.RefundToCustomer.Amount + split.RetainedByPlatform.Amount + split.TransferredToDealer.Amount;
    }

    private static decimal ChargeOf(DisputeTicket ticket, Booking booking, string currency)
    {
        if (ticket.Resolution!.DealerCharge is not { } charge)
            return 0m;
        if (!string.Equals(charge.CurrencyCode, currency, StringComparison.Ordinal))
            throw new DomainException($"Dispute {ticket.Id} charged the office in another currency than booking {booking.Id}.");
        return charge.Amount;
    }
}
