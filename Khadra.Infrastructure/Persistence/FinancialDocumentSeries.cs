namespace Khadra.Infrastructure.Persistence;

/// <summary>
/// One document-number series: a type, a kind of money and an Amman issue year (<c>PAY-2026</c>,
/// <c>TEST-RFD-2026</c>), and the last number it gave out. Written only by the atomic upsert that takes
/// the next number, inside the transaction that inserts the document.
/// </summary>
internal sealed class FinancialDocumentSeries
{
    public string SeriesKey { get; set; } = null!;
    public long LastNumber { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
