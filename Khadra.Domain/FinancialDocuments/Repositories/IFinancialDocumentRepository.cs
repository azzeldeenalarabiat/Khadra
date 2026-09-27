using Khadra.Domain.Common;

namespace Khadra.Domain.FinancialDocuments.Repositories;

/// <summary>
/// Issued documents and their voids (payments Phase 5). Append-only on both sides: there is no update
/// and no removal here, and the database refuses them too.
/// </summary>
public interface IFinancialDocumentRepository
{
    Task<FinancialDocument?> GetByIdAsync(Id id, CancellationToken cancellationToken = default);

    /// <summary>The highest version of a family — a payment's receipt, a refund's, or a booking's statement.</summary>
    Task<FinancialDocument?> LatestOfFamilyAsync(
        FinancialDocumentType type,
        Id subjectId,
        CancellationToken cancellationToken = default);

    /// <summary>Whether a void was recorded against the document.</summary>
    Task<bool> IsVoidedAsync(Id documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The latest version of every RECEIPT family on a booking, oldest money first: what a statement lists
    /// as the receipts issued so far.
    /// </summary>
    Task<IReadOnlyList<FinancialDocument>> ListLatestReceiptsForBookingAsync(
        Id bookingId,
        CancellationToken cancellationToken = default);

    void Add(FinancialDocument document);

    void AddVoid(FinancialDocumentVoid voided);
}

/// <summary>
/// The holds on document families that are owed but could not be issued (payments Phase 5). One row per
/// family, upserted: the caller finds the family's hold and fails or resolves it, or opens one.
/// </summary>
public interface IFinancialDocumentIssuanceHoldRepository
{
    /// <summary>The family's hold, resolved or not, tracked for an update.</summary>
    Task<FinancialDocumentIssuanceHold?> FindAsync(
        FinancialDocumentType type,
        Id subjectId,
        CancellationToken cancellationToken = default);

    void Add(FinancialDocumentIssuanceHold hold);
}

/// <summary>
/// The document number counters (payments Phase 5): one row per series, advanced INSIDE the caller's
/// transaction, so a document that is not inserted gives its number back when the transaction rolls back
/// and a series stays gapless. A database sequence would not: it leaves a gap on every rollback.
/// </summary>
public interface IFinancialDocumentSeries
{
    /// <summary>
    /// Takes the series' next number — 1 for a series never used — under a row lock held until the
    /// caller's transaction ends. Must be called inside a transaction.
    /// </summary>
    Task<long> TakeNextAsync(string seriesKey, DateTimeOffset now, CancellationToken cancellationToken = default);
}
