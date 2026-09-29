using System.Globalization;
using System.Text;
using Khadra.Application.Payments.Financials;
using Khadra.Domain.Bookings;
using Khadra.Domain.Disputes;
using Khadra.Domain.FinancialDocuments;
using Khadra.Domain.Payables;
using Khadra.Domain.Payments;

namespace Khadra.Application.FinancialDocuments.Composition;

/// <summary>
/// One fact that moves a booking's money — or, for a receipt's correction, the paperwork about money already
/// stated — with the instant it is stored under.
/// </summary>
/// <param name="Key">The record the fact is about: the payment, refund, ticket, booking, handover or correction.</param>
/// <param name="Facts">What about it the fingerprint covers, beyond its identity and instant.</param>
public sealed record Checkpoint(FinancialDocumentCause Kind, Guid Key, DateTimeOffset At, string Facts);

/// <summary>
/// The CLOSED list of facts that issue a new version of a booking statement: a payment captured, a refund
/// settled, a dispute resolved, the booking ended, cash recorded at a handover (owner, 2026-09-27), a
/// receipt corrected (owner, 2026-09-28; pre-launch item 181), and a customer's penalty kept from the deposit
/// (owner, 2026-09-30; pre-launch item 212). Nothing else is one — not a refund being recorded, sent or
/// refused, not a dispute opening, not a statement's own correction, and never a state that changes with the
/// clock alone, such as a deposit's window closing. A kept penalty is not the clock either: it is the office
/// payables ledger's RECORD of it, a row with its own instant, which the deposit's state is read from too.
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
    /// The newest fact: the statement's cause. Facts sharing an instant are ranked so the one that explains
    /// the others wins — a dispute decision over the ending it brings about, any money over a correction.
    /// </summary>
    public Checkpoint Latest => Newest(All) ?? throw new InvalidOperationException("No checkpoint to name.");

    /// <summary>
    /// The newest fact that MOVED money — every kind but <see cref="FinancialDocumentCause.ReceiptCorrected"/> —
    /// which is the instant a statement records as when its money moved. A correction is paperwork about money
    /// already stated: a version issued for one keeps the instant of the last money it states, and a booking
    /// with no correction reads exactly as before (<c>MoneyMovedAt == Latest.At</c>).
    /// </summary>
    public DateTimeOffset MoneyMovedAt =>
        Newest(All.Where(checkpoint => MovesMoney(checkpoint.Kind)))?.At
            ?? throw new InvalidOperationException("A statement's facts hold no money: a receipt was corrected on a booking with no captured payment.");

    /// <param name="payments">The booking's payments, with their refunds. Others are ignored.</param>
    /// <param name="resolvedTickets">The booking's resolved tickets. Others are ignored.</param>
    /// <param name="receipts">
    /// The booking's receipts as issued. Only the latest version of each receipt family counts, and it counts
    /// only when it is a correction; statements and other bookings' documents are ignored.
    /// </param>
    /// <param name="recorded">
    /// The booking's payable as the office payables ledger recorded it, if it has. It counts only when it kept a
    /// customer's penalty: the same record the deposit's <c>KeptAsPenalty</c> is read from, so a statement cannot
    /// state the deposit kept without this fact, nor this fact without it.
    /// </param>
    public static StatementCheckpoints Of(
        Booking booking,
        IEnumerable<Payment> payments,
        IEnumerable<DisputeTicket> resolvedTickets,
        IEnumerable<FinancialDocument> receipts,
        RecordedPayable? recorded = null)
    {
        ArgumentNullException.ThrowIfNull(booking);
        ArgumentNullException.ThrowIfNull(payments);
        ArgumentNullException.ThrowIfNull(resolvedTickets);
        ArgumentNullException.ThrowIfNull(receipts);

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

        // A receipt's correction (owner, 2026-09-28): the family's latest version, when that is a correction,
        // by identity and instant — an issued document never changes. A booking whose receipts were never
        // corrected gets no line here, so its fingerprint is what it always was.
        foreach (var correction in receipts
                     .Where(receipt => receipt.BookingId == booking.Id && receipt.Type.IsReceipt)
                     .GroupBy(receipt => (receipt.Type, receipt.SubjectId))
                     .Select(family => family.MaxBy(receipt => receipt.Version)!)
                     .Where(latest => latest.Cause == FinancialDocumentCause.Correction))
        {
            all.Add(new Checkpoint(FinancialDocumentCause.ReceiptCorrected, correction.Id.Value, correction.IssuedAt, string.Empty));
        }

        // A kept penalty (owner, 2026-09-30; pre-launch item 212): the payable by identity and the instant the ledger
        // recorded it. A payable's figures are frozen by the database, so nothing more needs fingerprinting; a
        // booking with no kept penalty gets no line here, so its fingerprint is what it always was.
        if (recorded is { } payable && payable.Outcome == PayableOutcome.PenaltyKept)
        {
            all.Add(new Checkpoint(FinancialDocumentCause.PenaltyKept, payable.PayableId.Value, payable.RecordedAt, string.Empty));
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

    /// <summary>The newest of some facts, by one ordering shared by the cause and the money instant.</summary>
    private static Checkpoint? Newest(IEnumerable<Checkpoint> checkpoints) =>
        checkpoints
            .OrderByDescending(checkpoint => checkpoint.At)
            .ThenByDescending(checkpoint => Rank(checkpoint.Kind))
            .ThenByDescending(checkpoint => checkpoint.Key)
            .FirstOrDefault();

    private static bool MovesMoney(FinancialDocumentCause kind) => kind != FinancialDocumentCause.ReceiptCorrected;

    private static int Rank(FinancialDocumentCause kind) =>
        // The last word on a deposit nobody disputed: it explains every other fact at its instant.
        kind == FinancialDocumentCause.PenaltyKept ? 6
        : kind == FinancialDocumentCause.DisputeResolved ? 5
        : kind == FinancialDocumentCause.BookingEnded ? 4
        : kind == FinancialDocumentCause.RefundSettled ? 3
        : kind == FinancialDocumentCause.CashRecorded ? 2
        : kind == FinancialDocumentCause.PaymentCaptured ? 1
        // Paperwork about money already stated: any money fact at the same instant names the statement.
        : kind == FinancialDocumentCause.ReceiptCorrected ? 0
        : throw new ArgumentOutOfRangeException(nameof(kind), kind.Name, "Not a statement checkpoint.");
}
