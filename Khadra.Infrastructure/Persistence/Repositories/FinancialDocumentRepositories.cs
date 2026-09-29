using System.Globalization;
using Khadra.Domain.Common;
using Khadra.Domain.FinancialDocuments;
using Khadra.Domain.FinancialDocuments.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Khadra.Infrastructure.Persistence.Repositories;

/// <summary>Issued documents and their voids (payments Phase 5). Append-only: nothing here updates or removes.</summary>
internal sealed class FinancialDocumentRepository(KhadraDbContext context) : IFinancialDocumentRepository
{
    public Task<FinancialDocument?> GetByIdAsync(Id id, CancellationToken cancellationToken = default) =>
        context.FinancialDocuments.FirstOrDefaultAsync(document => document.Id == id, cancellationToken);

    public Task<FinancialDocument?> LatestOfFamilyAsync(
        FinancialDocumentType type,
        Id subjectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        return context.FinancialDocuments
            .Where(document => document.Type == type && document.SubjectId == subjectId)
            .OrderByDescending(document => document.Version)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<bool> IsVoidedAsync(Id documentId, CancellationToken cancellationToken = default) =>
        context.FinancialDocumentVoids.AnyAsync(voided => voided.Id == documentId, cancellationToken);

    public Task<FinancialDocumentVoid?> VoidOfAsync(Id documentId, CancellationToken cancellationToken = default) =>
        context.FinancialDocumentVoids.FirstOrDefaultAsync(voided => voided.Id == documentId, cancellationToken);

    public Task<FinancialDocument?> FamilyMemberAsync(
        FinancialDocumentType type,
        Id subjectId,
        int version,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        return context.FinancialDocuments.FirstOrDefaultAsync(
            document => document.Type == type && document.SubjectId == subjectId && document.Version == version,
            cancellationToken);
    }

    public async Task<IReadOnlyList<FinancialDocument>> ListLatestReceiptsForBookingAsync(
        Id bookingId,
        CancellationToken cancellationToken = default)
    {
        var payment = FinancialDocumentType.PaymentReceipt;
        var refund = FinancialDocumentType.RefundReceipt;
        return await context.FinancialDocuments
            .Where(document => document.BookingId == bookingId && (document.Type == payment || document.Type == refund))
            .Where(document => !context.FinancialDocuments.Any(later =>
                later.Type == document.Type && later.SubjectId == document.SubjectId && later.Version > document.Version))
            .OrderBy(document => document.OccurredAt)
            .ThenBy(document => document.Id)
            .ToListAsync(cancellationToken);
    }

    public void Add(FinancialDocument document) => context.FinancialDocuments.Add(document);

    public void AddVoid(FinancialDocumentVoid voided) => context.FinancialDocumentVoids.Add(voided);
}

/// <summary>The stored renderings of issued documents (payments Phase 6). Append-only: nothing here updates or removes.</summary>
internal sealed class FinancialDocumentRenditionRepository(KhadraDbContext context) : IFinancialDocumentRenditionRepository
{
    public Task<FinancialDocumentRendition?> CurrentAsync(
        Id documentId,
        Language language,
        RenditionFormat format,
        RenditionKind kind,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(kind);
        return context.FinancialDocumentRenditions
            .Where(rendition => rendition.DocumentId == documentId
                && rendition.Language == language
                && rendition.Format == format
                && rendition.Kind == kind)
            .OrderByDescending(rendition => rendition.TemplateVersion)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FinancialDocumentRendition>> ListForDocumentAsync(Id documentId, CancellationToken cancellationToken = default) =>
        await context.FinancialDocumentRenditions
            .Where(rendition => rendition.DocumentId == documentId)
            .OrderBy(rendition => rendition.RenderedAt)
            .ThenBy(rendition => rendition.Id)
            .ToListAsync(cancellationToken);

    public void Add(FinancialDocumentRendition rendition) => context.FinancialDocumentRenditions.Add(rendition);
}

/// <summary>The emails owed for issued receipts (payments Phase 7): the outbox the email service works.</summary>
internal sealed class FinancialDocumentDeliveryRepository(KhadraDbContext context) : IFinancialDocumentDeliveryRepository
{
    public void Add(FinancialDocumentDelivery delivery) => context.FinancialDocumentDeliveries.Add(delivery);

    public async Task<IReadOnlyList<ClaimedFinancialDocumentDelivery>> ClaimDueAsync(
        DateTimeOffset now,
        TimeSpan lease,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var leaseUntil = now.Add(lease);
        var queued = FinancialDocumentDeliveryState.Queued;
        List<Id> claimed;

        if (context.Database.IsNpgsql())
        {
            // One statement: choose, lock, lease and count, as the notification outbox claims. SKIP LOCKED is what
            // makes two processes safe — a row another is claiming is passed over, never waited on and never taken
            // twice. Oldest due first, so a backlog drains in the order it built up.
            var ids = await context.Database
                .SqlQuery<Guid>($"""
                    UPDATE financial_document_deliveries AS d
                       SET claims = d.claims + 1,
                           next_attempt_at = {leaseUntil},
                           updated_at = {now}
                     WHERE d.id IN (
                           SELECT id FROM financial_document_deliveries
                            WHERE state = 'Queued' AND next_attempt_at <= {now}
                            ORDER BY next_attempt_at, id
                            LIMIT {batchSize}
                            FOR UPDATE SKIP LOCKED)
                    RETURNING d.id AS "Value"
                    """)
                .ToListAsync(cancellationToken);
            claimed = [.. ids.Select(Id.From)];
        }
        else
        {
            // SQLite (the persistence tests) has one writer at a time and no row locks, so the plain select-then-update
            // is already exclusive there. Same effect, same columns.
            claimed = await context.FinancialDocumentDeliveries
                .Where(delivery => delivery.State == queued && delivery.NextAttemptAt <= now)
                .OrderBy(delivery => delivery.NextAttemptAt)
                .Take(batchSize)
                .Select(delivery => delivery.Id)
                .ToListAsync(cancellationToken);
            await context.FinancialDocumentDeliveries
                .Where(delivery => claimed.Contains(delivery.Id))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(delivery => delivery.Claims, delivery => delivery.Claims + 1)
                        .SetProperty(delivery => delivery.NextAttemptAt, leaseUntil),
                    cancellationToken);
        }

        if (claimed.Count == 0)
            return [];

        // The count each claim left, read back while the lease still holds the rows — nobody else can claim them before
        // it runs out. It is what the sender checks before working a row, so a row whose lease ran out and was claimed
        // again by another process is left to that process. Worked oldest first.
        var counts = await context.FinancialDocumentDeliveries
            .AsNoTracking()
            .Where(delivery => claimed.Contains(delivery.Id))
            .Select(delivery => new { delivery.Id, delivery.Claims, delivery.QueuedAt })
            .ToListAsync(cancellationToken);
        return
        [
            .. counts
                .OrderBy(row => row.QueuedAt)
                .ThenBy(row => row.Id.Value)
                .Select(row => new ClaimedFinancialDocumentDelivery(row.Id, row.Claims)),
        ];
    }

    public Task<FinancialDocumentDelivery?> GetAsync(Id deliveryId, CancellationToken cancellationToken = default) =>
        context.FinancialDocumentDeliveries
            .Include(delivery => delivery.Attempts)
            .FirstOrDefaultAsync(delivery => delivery.Id == deliveryId, cancellationToken);

    public Task<bool> HasQueuedAsync(Id documentId, CancellationToken cancellationToken = default)
    {
        var queued = FinancialDocumentDeliveryState.Queued;
        return context.FinancialDocumentDeliveries
            .AnyAsync(delivery => delivery.DocumentId == documentId && delivery.State == queued, cancellationToken);
    }
}

/// <summary>The holds on document families that are owed a document (payments Phase 5).</summary>
internal sealed class FinancialDocumentIssuanceHoldRepository(KhadraDbContext context) : IFinancialDocumentIssuanceHoldRepository
{
    public Task<FinancialDocumentIssuanceHold?> FindAsync(
        FinancialDocumentType type,
        Id subjectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        return context.FinancialDocumentIssuanceHolds
            .FirstOrDefaultAsync(hold => hold.DocumentType == type && hold.SubjectId == subjectId, cancellationToken);
    }

    public void Add(FinancialDocumentIssuanceHold hold) => context.FinancialDocumentIssuanceHolds.Add(hold);
}

/// <summary>
/// The document number counters (payments Phase 5), advanced by ONE statement inside the caller's
/// transaction.
/// </summary>
/// <remarks>
/// <para>
/// <c>INSERT … ON CONFLICT DO UPDATE … RETURNING</c>: a series never used starts at 1, a series in use
/// moves on by one, and the row stays locked until the issuing transaction ends. A second issuer waits on
/// that lock and then reads the incremented value — never the same number — and if the document insert
/// after it fails, the rollback returns the number, so a series has no gaps. A database sequence would
/// leave one on every rollback, which is why this is a row and not a sequence.
/// </para>
/// <para>
/// Raw SQL, because the increment must happen in the database: a read-modify-write through the change
/// tracker would let two issuers compute the same number. The statement reads the same on PostgreSQL and
/// on SQLite (the persistence tests); only the instant's storage differs, as it does for every column.
/// </para>
/// </remarks>
internal sealed class FinancialDocumentSeriesCounter(KhadraDbContext context) : IFinancialDocumentSeries
{
    private const string Sql = """
        INSERT INTO financial_document_series (series_key, last_number, updated_at)
        VALUES (@series_key, 1, @updated_at)
        ON CONFLICT (series_key) DO UPDATE
            SET last_number = financial_document_series.last_number + 1,
                updated_at = excluded.updated_at
        RETURNING last_number
        """;

    public async Task<long> TakeNextAsync(string seriesKey, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seriesKey);
        if (seriesKey.Length > FinancialDocumentNumbers.MaxSeriesKeyLength)
            throw new ArgumentException($"A series key is at most {FinancialDocumentNumbers.MaxSeriesKeyLength} characters.", nameof(seriesKey));

        // Outside a transaction the number would be committed on its own, and a document that then failed
        // to insert would leave a gap for good.
        var transaction = context.Database.CurrentTransaction
            ?? throw new InvalidOperationException("A document number is taken only inside the transaction that inserts the document.");

        var command = context.Database.GetDbConnection().CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = Sql;
            AddParameter(command, "series_key", seriesKey);
            // Stored the way EF stores every instant on this provider: timestamptz in UTC on PostgreSQL,
            // UTC ticks on SQLite (KhadraDbContext.OnModelCreating).
            AddParameter(command, "updated_at", IsSqlite ? now.UtcTicks : now.ToUniversalTime());

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt64(result, CultureInfo.InvariantCulture);
        }
    }

    private bool IsSqlite =>
        string.Equals(context.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal);

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
