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
/// "Earlier" means resolved tickets opened BEFORE the one asked about. For a live ticket that is every
/// resolved one (a ticket can only open once the previous one closed); for a resolved ticket it keeps
/// its basis what it was when it was decided, whatever later tickets do.
/// </para>
/// </remarks>
public static class DisputedDeposit
{
    /// <param name="depositReleased">
    /// Whether the booking's deposit already went back because its dispute window closed cleanly
    /// (a <c>DisputeWindowClosed</c> refund on its deposit payment).
    /// </param>
    /// <param name="resolvedTickets">The booking's resolved tickets; only those opened before <paramref name="ticket"/> count.</param>
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
        var currency = deposit.CurrencyCode;

        var decided = 0m;
        var charged = 0m;
        foreach (var earlier in resolvedTickets.Where(other =>
                     other.Id != ticket.Id &&
                     other.BookingId == ticket.BookingId &&
                     other.OpenedAt < ticket.OpenedAt &&
                     other.Resolution is not null))
        {
            var split = earlier.Resolution!.Deposit;
            if (!string.Equals(split.DepositHeld.CurrencyCode, currency, StringComparison.Ordinal))
                throw new DomainException($"Dispute {earlier.Id} split a deposit in another currency than booking {booking.Id}.");

            decided += split.RefundToCustomer.Amount + split.RetainedByPlatform.Amount + split.TransferredToDealer.Amount;

            if (earlier.Resolution.DealerCharge is { } earlierCharge)
            {
                if (!string.Equals(earlierCharge.CurrencyCode, currency, StringComparison.Ordinal))
                    throw new DomainException($"Dispute {earlier.Id} charged the office in another currency than booking {booking.Id}.");
                charged += earlierCharge.Amount;
            }
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
}
