using Khadra.Application.FinancialDocuments.ReadModels;
using Khadra.Domain.Bookings;
using Khadra.Domain.Disputes;
using Khadra.Domain.FinancialDocuments;
using Khadra.Domain.Payments;
using Khadra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Reporting;

/// <summary>
/// Finds the document families owed a document (payments Phase 5). The durable facts are the queue: a
/// captured payment with no receipt row, a settled refund with none, a booking whose checkpoints are newer
/// than its latest statement — so nothing is ever recorded as "due", and nothing can be lost.
/// </summary>
/// <remarks>
/// <para>
/// <b>Statements</b> follow the closed list of checkpoints (owner, 2026-09-27) and never the clock: a
/// booking is looked at when a checkpoint instant is later than its latest statement's
/// <c>covers_through</c>, and — for the late-commit margin only — when that statement was issued moments
/// ago, since a fact can commit just after a statement read the records while carrying an earlier instant.
/// Either way the checkpoint fingerprint decides; a booking whose facts have not changed is never issued a
/// version, however many passes run.
/// </para>
/// <para>
/// A family on hold waits for its next attempt. A hold for a missing issuer stops waiting the moment an
/// issuer is configured — which only a restart brings — so a newly configured identity issues at once.
/// </para>
/// </remarks>
internal sealed class FinancialDocumentCandidateReader(KhadraDbContext context) : IFinancialDocumentCandidateReader
{
    public async Task<IReadOnlyList<IssuanceCandidate>> ListAsync(
        DateTimeOffset now,
        TimeSpan lateCommitMargin,
        bool issuerConfigured,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
            return [];

        // One after another: every read shares this scope's context.
        var candidates = new List<IssuanceCandidate>();
        candidates.AddRange(await PaymentReceiptsAsync(now, issuerConfigured, limit, cancellationToken));
        if (candidates.Count < limit)
            candidates.AddRange(await RefundReceiptsAsync(now, issuerConfigured, limit - candidates.Count, cancellationToken));
        if (candidates.Count < limit)
            candidates.AddRange(await FirstStatementsAsync(now, issuerConfigured, limit - candidates.Count, cancellationToken));
        if (candidates.Count < limit)
            candidates.AddRange(await NextStatementsAsync(now, lateCommitMargin, issuerConfigured, limit - candidates.Count, cancellationToken));
        return candidates;
    }

    /// <summary>Every captured payment with no receipt at all, oldest capture first.</summary>
    private async Task<IEnumerable<IssuanceCandidate>> PaymentReceiptsAsync(
        DateTimeOffset now,
        bool issuerConfigured,
        int limit,
        CancellationToken cancellationToken)
    {
        var type = FinancialDocumentType.PaymentReceipt;
        var (applied, orphaned) = (PaymentStatus.Applied, PaymentStatus.Orphaned);
        var waiting = Waiting(type, now, issuerConfigured);

        var rows = await context.Payments
            .Where(payment => payment.Status == applied || payment.Status == orphaned)
            .Where(payment => !context.FinancialDocuments.Any(document => document.Type == type && document.SubjectId == payment.Id))
            .Where(payment => !waiting.Any(hold => hold.SubjectId == payment.Id))
            .OrderBy(payment => payment.AppliedAt ?? payment.OrphanedAt ?? payment.CapturedAt)
            .ThenBy(payment => payment.Id)
            .Select(payment => new { payment.Id, payment.BookingId })
            .Take(limit)
            .ToListAsync(cancellationToken);
        return rows.Select(row => new IssuanceCandidate(type, row.Id, row.BookingId));
    }

    /// <summary>
    /// Every settled refund with no receipt, oldest settlement first — once its payment has a receipt, which
    /// the refund receipt links to and never alters. The work is listed once per pass, so a refund whose
    /// payment's receipt is issued in this pass gets its own in the next one; the booking's documents say
    /// "being prepared" in between.
    /// </summary>
    private async Task<IEnumerable<IssuanceCandidate>> RefundReceiptsAsync(
        DateTimeOffset now,
        bool issuerConfigured,
        int limit,
        CancellationToken cancellationToken)
    {
        var type = FinancialDocumentType.RefundReceipt;
        var paymentReceipt = FinancialDocumentType.PaymentReceipt;
        var settled = RefundStatus.Settled;
        var waiting = Waiting(type, now, issuerConfigured);

        var rows = await context.Set<Refund>()
            .Where(refund => refund.Status == settled)
            .Where(refund => !context.FinancialDocuments.Any(document => document.Type == type && document.SubjectId == refund.Id))
            .Where(refund => context.FinancialDocuments.Any(document => document.Type == paymentReceipt && document.SubjectId == refund.PaymentId))
            .Where(refund => !waiting.Any(hold => hold.SubjectId == refund.Id))
            .Join(context.Payments, refund => refund.PaymentId, payment => payment.Id, (refund, payment) => new { refund.Id, refund.SettledAt, payment.BookingId })
            .OrderBy(row => row.SettledAt)
            .ThenBy(row => row.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return rows.Select(row => new IssuanceCandidate(type, row.Id, row.BookingId));
    }

    /// <summary>Every booking with money captured and no statement yet, oldest first money first.</summary>
    private async Task<IEnumerable<IssuanceCandidate>> FirstStatementsAsync(
        DateTimeOffset now,
        bool issuerConfigured,
        int limit,
        CancellationToken cancellationToken)
    {
        var type = FinancialDocumentType.BookingStatement;
        var (applied, orphaned) = (PaymentStatus.Applied, PaymentStatus.Orphaned);
        var waiting = Waiting(type, now, issuerConfigured);

        var rows = await context.Payments
            .Where(payment => payment.Status == applied || payment.Status == orphaned)
            .Where(payment => !context.FinancialDocuments.Any(document => document.Type == type && document.SubjectId == payment.BookingId))
            .Where(payment => !waiting.Any(hold => hold.SubjectId == payment.BookingId))
            .GroupBy(payment => payment.BookingId)
            .Select(group => new
            {
                BookingId = group.Key,
                First = group.Min(payment => payment.AppliedAt ?? payment.OrphanedAt ?? payment.CapturedAt),
            })
            .OrderBy(row => row.First)
            .ThenBy(row => row.BookingId)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return rows.Select(row => new IssuanceCandidate(type, row.BookingId, row.BookingId));
    }

    /// <summary>
    /// Every booking whose latest statement a checkpoint has overtaken, or that was issued within the
    /// late-commit margin. The fingerprint decides which of them really is new.
    /// </summary>
    private async Task<IEnumerable<IssuanceCandidate>> NextStatementsAsync(
        DateTimeOffset now,
        TimeSpan lateCommitMargin,
        bool issuerConfigured,
        int limit,
        CancellationToken cancellationToken)
    {
        var type = FinancialDocumentType.BookingStatement;
        var (applied, orphaned) = (PaymentStatus.Applied, PaymentStatus.Orphaned);
        var settled = RefundStatus.Settled;
        var resolved = DisputeStatus.Resolved;
        var recently = now - lateCommitMargin;
        var waiting = Waiting(type, now, issuerConfigured);

        var rows = await context.FinancialDocuments
            .Where(statement => statement.Type == type)
            .Where(statement => !context.FinancialDocuments.Any(later =>
                later.Type == type && later.SubjectId == statement.SubjectId && later.Version > statement.Version))
            .Where(statement => !waiting.Any(hold => hold.SubjectId == statement.SubjectId))
            .Where(statement =>
                statement.IssuedAt > recently
                || context.Payments.Any(payment =>
                    payment.BookingId == statement.BookingId
                    && (payment.Status == applied || payment.Status == orphaned)
                    && (payment.AppliedAt ?? payment.OrphanedAt ?? payment.CapturedAt) > statement.CoversThrough)
                || context.Set<Refund>().Any(refund =>
                    refund.Status == settled
                    && refund.SettledAt > statement.CoversThrough
                    && context.Payments.Any(payment => payment.Id == refund.PaymentId && payment.BookingId == statement.BookingId))
                || context.DisputeTickets.Any(ticket =>
                    ticket.BookingId == statement.BookingId && ticket.Status == resolved && ticket.ClosedAt > statement.CoversThrough)
                || context.Bookings.Any(booking =>
                    booking.Id == statement.BookingId && booking.FinishedAt > statement.CoversThrough)
                || context.Set<HandoverRecord>().Any(handover =>
                    handover.BookingId == statement.BookingId && handover.CashCollected != null && handover.RecordedAt > statement.CoversThrough))
            .OrderBy(statement => statement.CoversThrough)
            .ThenBy(statement => statement.Id)
            .Select(statement => statement.BookingId)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return rows.Select(bookingId => new IssuanceCandidate(type, bookingId, bookingId));
    }

    /// <summary>A family's open hold that is not due another attempt yet.</summary>
    private IQueryable<FinancialDocumentIssuanceHold> Waiting(FinancialDocumentType type, DateTimeOffset now, bool issuerConfigured)
    {
        var missingIssuer = IssuanceHoldReason.IssuerNotConfigured;
        return context.FinancialDocumentIssuanceHolds.Where(hold =>
            hold.DocumentType == type
            && hold.ResolvedAt == null
            && hold.NextAttemptAt > now
            && !(issuerConfigured && hold.Reason == missingIssuer));
    }
}
