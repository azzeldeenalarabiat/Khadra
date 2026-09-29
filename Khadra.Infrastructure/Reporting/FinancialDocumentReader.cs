using Khadra.Application.Common;
using Khadra.Application.FinancialDocuments.ReadModels;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Khadra.Domain.FinancialDocuments;
using Khadra.Domain.Payments;
using Khadra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Reporting;

/// <summary>
/// The customer's and the administrator's readings of issued documents and holds (payments Phase 5).
/// </summary>
/// <remarks>
/// A document's standing is worked out here, in the same query that reads it — voided when a void row
/// exists for it, superseded when a later version of its family exists — because it is never stored: a
/// document row never changes. Every list is ordered (issued at DESC, id DESC), a total order, so no page
/// boundary drops or repeats a document.
/// </remarks>
internal sealed class FinancialDocumentReader(KhadraDbContext context) : IFinancialDocumentReader
{
    public async Task<PagedResult<FinancialDocumentRecord>> ListForCustomerAsync(
        Id customerId,
        FinancialDocumentType? type,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        var query = context.FinancialDocuments.AsNoTracking().Where(document => document.CustomerId == customerId);
        if (type is not null)
            query = query.Where(document => document.Type == type);
        return await PageAsync(query, page, cancellationToken);
    }

    public async Task<PagedResult<FinancialDocumentRecord>> ListAsync(
        AdminFinancialDocumentFilter filter,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(page);

        var query = context.FinancialDocuments.AsNoTracking();
        if (filter.Type is { } type)
            query = query.Where(document => document.Type == type);
        if (filter.Number is { } number)
            query = query.Where(document => document.Number == number);
        if (filter.Reference is { } reference)
            query = query.Where(document => document.BookingReference == reference);
        if (filter.IssuedFrom is { } from)
            query = query.Where(document => document.IssuedAt >= from);
        if (filter.IssuedBefore is { } before)
            query = query.Where(document => document.IssuedAt < before);

        if (filter.Status == FinancialDocumentStatus.Voided)
        {
            query = query.Where(document => context.FinancialDocumentVoids.Any(voided => voided.Id == document.Id));
        }
        else if (filter.Status == FinancialDocumentStatus.Superseded)
        {
            query = query.Where(document =>
                !context.FinancialDocumentVoids.Any(voided => voided.Id == document.Id)
                && context.FinancialDocuments.Any(later =>
                    later.Type == document.Type && later.SubjectId == document.SubjectId && later.Version > document.Version));
        }
        else if (filter.Status == FinancialDocumentStatus.Current)
        {
            query = query.Where(document =>
                !context.FinancialDocumentVoids.Any(voided => voided.Id == document.Id)
                && !context.FinancialDocuments.Any(later =>
                    later.Type == document.Type && later.SubjectId == document.SubjectId && later.Version > document.Version));
        }

        return await PageAsync(query, page, cancellationToken);
    }

    public async Task<IReadOnlyList<FinancialDocumentRecord>> ListForBookingAsync(Id bookingId, CancellationToken cancellationToken = default) =>
        await ListAsync(context.FinancialDocuments.AsNoTracking().Where(document => document.BookingId == bookingId), cancellationToken);

    public async Task<IReadOnlyList<FinancialDocumentRecord>> ListForPaymentAsync(Id paymentId, CancellationToken cancellationToken = default)
    {
        Id? payment = paymentId;
        return await ListAsync(context.FinancialDocuments.AsNoTracking().Where(document => document.PaymentId == payment), cancellationToken);
    }

    public async Task<FinancialDocumentRecord?> GetAsync(Id documentId, CancellationToken cancellationToken = default)
    {
        var found = await ListAsync(context.FinancialDocuments.AsNoTracking().Where(document => document.Id == documentId), cancellationToken);
        return found.Count == 0 ? null : found[0];
    }

    public async Task<IReadOnlyList<FinancialDocumentRecord>> FamilyAsync(
        FinancialDocumentType type,
        Id subjectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        var family = await ListAsync(
            context.FinancialDocuments.AsNoTracking().Where(document => document.Type == type && document.SubjectId == subjectId),
            cancellationToken);
        return [.. family.OrderBy(document => document.Version)];
    }

    public async Task<FinancialDocumentVoidRecord?> VoidOfAsync(Id documentId, CancellationToken cancellationToken = default)
    {
        var voided = await context.FinancialDocumentVoids
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == documentId, cancellationToken);
        if (voided is null)
            return null;

        // Past the soft-delete filter, deliberately: a void names the administrator who made it for as long
        // as the void exists, which is for good, whatever becomes of their account.
        var name = await context.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(user => user.Id == voided.VoidedByAdminId)
            .Select(user => user.Name.Value)
            .FirstOrDefaultAsync(cancellationToken);
        return new FinancialDocumentVoidRecord(documentId, voided.VoidedAt, voided.VoidedByAdminId, name, voided.Reason);
    }

    public async Task<IReadOnlyList<FinancialDocumentRenditionRecord>> RenditionsOfAsync(Id documentId, CancellationToken cancellationToken = default)
    {
        var renditions = await context.FinancialDocumentRenditions
            .AsNoTracking()
            .Where(rendition => rendition.DocumentId == documentId)
            .OrderBy(rendition => rendition.RenderedAt)
            .ThenBy(rendition => rendition.Id)
            .ToListAsync(cancellationToken);
        return
        [
            .. renditions.Select(rendition => new FinancialDocumentRenditionRecord(
                rendition.Language,
                rendition.Format,
                rendition.Kind,
                rendition.TemplateVersion,
                rendition.RendererVersion,
                rendition.ContentSha256,
                rendition.SizeBytes,
                rendition.RenderedAt,
                rendition.SnapshotSha256)),
        ];
    }

    public async Task<IReadOnlyList<PendingFinancialDocumentRecord>> PendingForBookingAsync(
        Id bookingId,
        CancellationToken cancellationToken = default)
    {
        var (paymentReceipt, refundReceipt, statement) =
            (FinancialDocumentType.PaymentReceipt, FinancialDocumentType.RefundReceipt, FinancialDocumentType.BookingStatement);
        var (applied, orphaned, settled, resolved) =
            (PaymentStatus.Applied, PaymentStatus.Orphaned, RefundStatus.Settled, DisputeStatus.Resolved);

        // One after another: every read shares this request's context.
        var captures = await context.Payments
            .AsNoTracking()
            .Where(payment => payment.BookingId == bookingId && (payment.Status == applied || payment.Status == orphaned))
            .Select(payment => new
            {
                payment.Id,
                At = payment.AppliedAt ?? payment.OrphanedAt ?? payment.CapturedAt,
                HasReceipt = context.FinancialDocuments.Any(document => document.Type == paymentReceipt && document.SubjectId == payment.Id),
            })
            .ToListAsync(cancellationToken);
        if (captures.Count == 0)
            return [];

        var refunds = await context.Set<Refund>()
            .AsNoTracking()
            .Where(refund => refund.Status == settled
                && context.Payments.Any(payment => payment.Id == refund.PaymentId && payment.BookingId == bookingId))
            .Select(refund => new
            {
                refund.Id,
                refund.SettledAt,
                HasReceipt = context.FinancialDocuments.Any(document => document.Type == refundReceipt && document.SubjectId == refund.Id),
            })
            .ToListAsync(cancellationToken);
        var covered = await context.FinancialDocuments
            .AsNoTracking()
            .Where(document => document.Type == statement && document.SubjectId == bookingId)
            .OrderByDescending(document => document.Version)
            .Select(document => document.CoversThrough)
            .FirstOrDefaultAsync(cancellationToken);
        var decided = await context.DisputeTickets
            .AsNoTracking()
            .Where(ticket => ticket.BookingId == bookingId && ticket.Status == resolved && ticket.ClosedAt != null)
            .MaxAsync(ticket => ticket.ClosedAt, cancellationToken);
        var finished = await context.Bookings
            .AsNoTracking()
            .Where(booking => booking.Id == bookingId)
            .Select(booking => booking.FinishedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var cash = await context.Set<HandoverRecord>()
            .AsNoTracking()
            .Where(handover => handover.BookingId == bookingId && handover.CashCollected != null)
            .MaxAsync(handover => (DateTimeOffset?)handover.RecordedAt, cancellationToken);
        // A RECEIPT's correction (owner, 2026-09-28) — never a statement's own, which follows what it covers.
        var correction = FinancialDocumentCause.Correction;
        var corrected = await context.FinancialDocuments
            .AsNoTracking()
            .Where(document => document.BookingId == bookingId
                && (document.Type == paymentReceipt || document.Type == refundReceipt)
                && document.Cause == correction)
            .MaxAsync(document => (DateTimeOffset?)document.IssuedAt, cancellationToken);

        var pending = new List<PendingFinancialDocumentRecord>();
        pending.AddRange(captures
            .Where(capture => !capture.HasReceipt && capture.At is not null)
            .Select(capture => new PendingFinancialDocumentRecord(paymentReceipt, capture.Id, capture.At!.Value)));
        pending.AddRange(refunds
            .Where(refund => !refund.HasReceipt && refund.SettledAt is not null)
            .Select(refund => new PendingFinancialDocumentRecord(refundReceipt, refund.Id, refund.SettledAt!.Value)));

        // The statement is behind when no version exists yet, or when a checkpoint is newer than the latest
        // version covers — on hold or not: the customer is told only that it is being prepared. It is dated
        // by the last money it will state; a receipt's correction makes it behind without moving any.
        var moneyMoved = new[] { captures.Max(capture => capture.At), refunds.Max(refund => refund.SettledAt), decided, finished, cash }
            .Where(instant => instant is not null)
            .Max();
        var newest = corrected is { } correctedAt && (moneyMoved is null || correctedAt > moneyMoved) ? corrected : moneyMoved;
        if (moneyMoved is { } dated && newest is { } behindFrom && (covered is null || behindFrom > covered))
            pending.Add(new PendingFinancialDocumentRecord(statement, bookingId, dated));

        return [.. pending.OrderBy(entry => entry.OccurredAt).ThenBy(entry => entry.Type.Id)];
    }

    public async Task<PagedResult<FinancialDocumentHoldRecord>> ListOpenHoldsAsync(PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        var query = OpenHolds();
        var total = await query.CountAsync(cancellationToken);
        if (total == 0)
            return PagedResult.Empty<FinancialDocumentHoldRecord>(page.Page, page.PageSize);

        var holds = await query
            .OrderBy(hold => hold.FirstFailedAt)
            .ThenBy(hold => hold.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<FinancialDocumentHoldRecord>(await WithReferencesAsync(holds, cancellationToken), page.Page, page.PageSize, total);
    }

    public async Task<IReadOnlyList<FinancialDocumentHoldRecord>> OpenHoldsForBookingAsync(Id bookingId, CancellationToken cancellationToken = default)
    {
        var holds = await OpenHolds()
            .Where(hold => hold.BookingId == bookingId)
            .OrderBy(hold => hold.FirstFailedAt)
            .ThenBy(hold => hold.Id)
            .ToListAsync(cancellationToken);
        return await WithReferencesAsync(holds, cancellationToken);
    }

    public async Task<FinancialDocumentHoldsSummary> OpenHoldsSummaryAsync(CancellationToken cancellationToken = default)
    {
        var holds = await OpenHolds()
            .OrderBy(hold => hold.FirstFailedAt)
            .ThenBy(hold => hold.Id)
            .Select(hold => new { hold.Id, hold.BookingId, hold.FirstFailedAt })
            .ToListAsync(cancellationToken);
        if (holds.Count == 0)
            return FinancialDocumentHoldsSummary.None;

        var firstBookings = holds.Select(hold => hold.BookingId).Distinct().Take(3).ToList();
        var references = await ReferencesAsync(firstBookings, cancellationToken);
        return new FinancialDocumentHoldsSummary(
            holds.Count,
            [.. holds.Select(hold => hold.Id)],
            [.. firstBookings.Where(references.ContainsKey).Select(id => references[id])],
            holds[0].FirstFailedAt);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────

    private IQueryable<FinancialDocumentIssuanceHold> OpenHolds() =>
        context.FinancialDocumentIssuanceHolds.AsNoTracking().Where(hold => hold.ResolvedAt == null);

    private async Task<PagedResult<FinancialDocumentRecord>> PageAsync(
        IQueryable<FinancialDocument> query,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        var total = await query.CountAsync(cancellationToken);
        if (total == 0)
            return PagedResult.Empty<FinancialDocumentRecord>(page.Page, page.PageSize);

        var rows = await WithStanding(Ordered(query))
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<FinancialDocumentRecord>([.. rows.Select(row => Record(row.Document, row.Voided, row.Superseded))], page.Page, page.PageSize, total);
    }

    private async Task<IReadOnlyList<FinancialDocumentRecord>> ListAsync(IQueryable<FinancialDocument> query, CancellationToken cancellationToken)
    {
        var rows = await WithStanding(Ordered(query)).ToListAsync(cancellationToken);
        return [.. rows.Select(row => Record(row.Document, row.Voided, row.Superseded))];
    }

    /// <summary>Newest first, the id breaking the tie: a non-total order lets a page boundary drop a document.</summary>
    internal static IOrderedQueryable<FinancialDocument> Ordered(IQueryable<FinancialDocument> query) =>
        query.OrderByDescending(document => document.IssuedAt).ThenByDescending(document => document.Id);

    private IQueryable<Standing> WithStanding(IQueryable<FinancialDocument> query) =>
        query.Select(document => new Standing
        {
            Document = document,
            Voided = context.FinancialDocumentVoids.Any(voided => voided.Id == document.Id),
            Superseded = context.FinancialDocuments.Any(later =>
                later.Type == document.Type && later.SubjectId == document.SubjectId && later.Version > document.Version),
        });

    private async Task<IReadOnlyList<FinancialDocumentHoldRecord>> WithReferencesAsync(
        List<FinancialDocumentIssuanceHold> holds,
        CancellationToken cancellationToken)
    {
        var references = await ReferencesAsync([.. holds.Select(hold => hold.BookingId).Distinct()], cancellationToken);
        return
        [
            .. holds.Select(hold => new FinancialDocumentHoldRecord(
                hold.Id,
                hold.DocumentType,
                hold.SubjectId,
                hold.BookingId,
                references.GetValueOrDefault(hold.BookingId),
                hold.Reason,
                hold.Attempts,
                hold.FirstFailedAt,
                hold.LastFailedAt,
                hold.NextAttemptAt,
                hold.LastError)),
        ];
    }

    private async Task<Dictionary<Id, string>> ReferencesAsync(List<Id> bookingIds, CancellationToken cancellationToken) =>
        bookingIds.Count == 0
            ? []
            : await context.Bookings
                .AsNoTracking()
                .Where(booking => bookingIds.Contains(booking.Id))
                .Select(booking => new { booking.Id, Reference = booking.Reference.Value })
                .ToDictionaryAsync(booking => booking.Id, booking => booking.Reference, cancellationToken);

    private static FinancialDocumentRecord Record(FinancialDocument document, bool voided, bool superseded) =>
        new(
            document.Id,
            document.Type,
            document.Number,
            document.Version,
            FinancialDocumentStatus.Of(voided, superseded),
            document.SubjectId,
            document.BookingId,
            document.BookingReference,
            document.CustomerId,
            document.DealerId,
            document.PaymentId,
            document.RefundId,
            document.PreviousVersionId,
            document.RelatedDocumentId,
            document.Cause,
            document.OccurredAt,
            document.IssuedAt,
            document.CoversThrough,
            document.CheckpointFingerprint,
            Money.Create(document.HeadlineAmount.Amount, document.HeadlineAmount.CurrencyCode),
            document.Provider,
            document.SnapshotSchemaVersion,
            document.Snapshot,
            document.ContentSha256);

    private sealed class Standing
    {
        public required FinancialDocument Document { get; init; }
        public required bool Voided { get; init; }
        public required bool Superseded { get; init; }
    }
}
