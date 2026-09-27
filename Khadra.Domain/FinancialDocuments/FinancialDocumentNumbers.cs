using System.Globalization;

namespace Khadra.Domain.FinancialDocuments;

/// <summary>
/// How a document number reads: <c>PAY-2026-000001</c>, and <c>TEST-PAY-2026-000001</c> for sandbox
/// money (owner, 2026-09-27: a test document must never be confused with a real one).
/// </summary>
/// <remarks>
/// One series per document type, kind of money and Amman ISSUE year. The number itself is taken from a
/// counter row in the same transaction that inserts the document, which is what keeps a series gapless;
/// this class only says how a series is named and how a number is written.
/// </remarks>
public static class FinancialDocumentNumbers
{
    public const string TestPrefix = "TEST-";

    /// <summary>Six digits, growing to seven if a year ever needs it — never truncated.</summary>
    public const int MinimumDigits = 6;

    /// <summary>The longest number the column holds: a TEST- prefix, a type, a year and seven digits fit easily.</summary>
    public const int MaxLength = 32;

    /// <summary>The longest series key the counter table holds.</summary>
    public const int MaxSeriesKeyLength = 24;

    public static string SeriesKey(FinancialDocumentType type, int issueYear, bool isTest)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (issueYear is < 2000 or > 9999)
            throw new ArgumentOutOfRangeException(nameof(issueYear), issueYear, "An issue year has four digits.");

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(isTest ? TestPrefix : string.Empty)}{type.NumberPrefix}-{issueYear:D4}");
    }

    public static string Format(string seriesKey, long sequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seriesKey);
        if (sequence < 1)
            throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "A series starts at 1.");

        return string.Create(CultureInfo.InvariantCulture, $"{seriesKey}-{sequence.ToString("D" + MinimumDigits, CultureInfo.InvariantCulture)}");
    }
}
