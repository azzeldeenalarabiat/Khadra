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
