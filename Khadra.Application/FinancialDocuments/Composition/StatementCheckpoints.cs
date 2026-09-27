using System.Globalization;
using System.Text;
using Khadra.Domain.Bookings;
using Khadra.Domain.Disputes;
using Khadra.Domain.FinancialDocuments;
using Khadra.Domain.Payments;

namespace Khadra.Application.FinancialDocuments.Composition;

/// <summary>One fact that moves a booking's money, with the instant it is stored under.</summary>
/// <param name="Key">The record the fact is about: the payment, refund, ticket, booking or handover.</param>
/// <param name="Facts">What about it the fingerprint covers, beyond its identity and instant.</param>
public sealed record Checkpoint(FinancialDocumentCause Kind, Guid Key, DateTimeOffset At, string Facts);

/// <summary>
/// The CLOSED list of facts that issue a new version of a booking statement (owner, 2026-09-27): a payment
/// captured, a refund settled, a dispute resolved, the booking ended, cash recorded at a handover. Nothing
/// else is one — not a refund being recorded, sent or refused, not a dispute opening, and never a state
/// that changes with the clock alone, such as a deposit's window closing.
/// </summary>
/// <remarks>
/// <para>
/// A statement stores what it covered: the latest instant among these facts (<see cref="CoversThrough"/>)
/// and a fingerprint of the facts themselves (<see cref="Fingerprint"/>). A new version is issued only when
/// the fingerprint differs from the latest version's — which is why every fact here is one that never
/// changes once written, and why the fingerprint holds no figure a customer may not see: a dispute
/// decision is fingerprinted by its ticket and instant, never by the office's or the platform's share.
/// </para>
/// <para>
/// Built from COMMITTED records only. A booking read through the clock-settling repository can carry an
/// in-memory ending stamped with the current instant, which would move on every pass; the issuer reads
/// the booking as stored.
/// </para>
/// </remarks>
public sealed class StatementCheckpoints
{
    private StatementCheckpoints(IReadOnlyList<Checkpoint> all)
    {
        All = all;
        Fingerprint = FinancialDocument.Sha256(Canonical(all));
    }

    /// <summary>Every checkpoint, ordered by kind and record.</summary>
    public IReadOnlyList<Checkpoint> All { get; }

    /// <summary>SHA-256 (lowercase hex) of the checkpoint facts: what decides whether a statement is new.</summary>
    public string Fingerprint { get; }

    public bool IsEmpty => All.Count == 0;

    /// <summary>The latest instant among the facts: what a statement records it covers.</summary>
    public DateTimeOffset CoversThrough =>
        IsEmpty ? throw new InvalidOperationException("No checkpoint to cover.") : All.Max(checkpoint => checkpoint.At);

    /// <summary>
    /// The newest fact: the statement's cause and money instant. Facts sharing an instant are ranked so the
    /// one that explains the others wins — a dispute decision over the ending it brings about.
    /// </summary>
    public Checkpoint Latest =>
        IsEmpty
            ? throw new InvalidOperationException("No checkpoint to name.")
            : All.OrderByDescending(checkpoint => checkpoint.At)
                .ThenByDescending(checkpoint => Rank(checkpoint.Kind))
                .ThenByDescending(checkpoint => checkpoint.Key)
                .First();

    /// <param name="payments">The booking's payments, with their refunds. Others are ignored.</param>
    /// <param name="resolvedTickets">The booking's resolved tickets. Others are ignored.</param>
    public static StatementCheckpoints Of(
        Booking booking,
        IEnumerable<Payment> payments,
        IEnumerable<DisputeTicket> resolvedTickets)
    {
        ArgumentNullException.ThrowIfNull(booking);
        ArgumentNullException.ThrowIfNull(payments);
        ArgumentNullException.ThrowIfNull(resolvedTickets);

        var all = new List<Checkpoint>();
        foreach (var payment in payments.Where(payment => payment.BookingId == booking.Id && payment.Status.IsCaptured))
        {
            var captured = payment.AppliedAt ?? payment.OrphanedAt ?? payment.CapturedAt
                ?? throw new InvalidOperationException($"Payment {payment.Id.Value} is captured with no capture instant.");
            var amount = payment.AmountCaptured ?? payment.Amount;
            all.Add(new Checkpoint(
                FinancialDocumentCause.PaymentCaptured,
                payment.Id.Value,
                captured,
                $"{payment.Status.Name}|{SnapshotJson.Amount(amount.Amount)}|{amount.CurrencyCode}"));

            foreach (var refund in payment.Refunds.Where(refund => refund.Status == RefundStatus.Settled))
            {
                var settled = refund.SettledAt
                    ?? throw new InvalidOperationException($"Refund {refund.Id.Value} is settled with no settlement instant.");
                all.Add(new Checkpoint(
                    FinancialDocumentCause.RefundSettled,
                    refund.Id.Value,
                    settled,
                    $"{SnapshotJson.Amount(refund.Amount.Amount)}|{refund.Amount.CurrencyCode}"));
            }
        }

        foreach (var ticket in resolvedTickets.Where(ticket =>
                     ticket.BookingId == booking.Id && ticket.Status == DisputeStatus.Resolved && ticket.Resolution is not null))
        {
            // Identity and instant only: the shares are immutable once decided, and a fingerprint must not
            // encode the office's or the platform's share (decision 3).
            all.Add(new Checkpoint(
                FinancialDocumentCause.DisputeResolved,
                ticket.Id.Value,
                ticket.ClosedAt ?? ticket.Resolution!.ResolvedAt,
                string.Empty));
        }

        if (booking.FinishedAt is { } finished)
        {
            all.Add(new Checkpoint(FinancialDocumentCause.BookingEnded, booking.Id.Value, finished, booking.Status.Name));
        }

        foreach (var handover in booking.Handovers.Where(handover => handover.CashCollected is not null))
        {
            var cash = handover.CashCollected!;
            all.Add(new Checkpoint(
                FinancialDocumentCause.CashRecorded,
                handover.Id.Value,
                handover.RecordedAt,
                $"{handover.Type.Name}|{SnapshotJson.Amount(cash.Amount)}|{cash.CurrencyCode}"));
        }

        return new StatementCheckpoints(
        [
            .. all.OrderBy(checkpoint => checkpoint.Kind.Id).ThenBy(checkpoint => checkpoint.Key),
        ]);
    }

    /// <summary>
    /// One line per fact, in a fixed order, instants to the microsecond — the precision PostgreSQL stores,
    /// so a fact reads the same before and after a round trip.
    /// </summary>
    private static string Canonical(IReadOnlyList<Checkpoint> all)
    {
        var text = new StringBuilder();
        foreach (var checkpoint in all)
        {
            text.Append(checkpoint.Kind.Name).Append('|')
                .Append(checkpoint.Key.ToString("D", CultureInfo.InvariantCulture)).Append('|')
                .Append(checkpoint.At.UtcDateTime.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'ffffff'Z'", CultureInfo.InvariantCulture)).Append('|')
                .Append(checkpoint.Facts).Append('\n');
        }

        return text.ToString();
    }

    private static int Rank(FinancialDocumentCause kind) =>
        kind == FinancialDocumentCause.DisputeResolved ? 5
        : kind == FinancialDocumentCause.BookingEnded ? 4
        : kind == FinancialDocumentCause.RefundSettled ? 3
        : kind == FinancialDocumentCause.CashRecorded ? 2
        : 1;
}
