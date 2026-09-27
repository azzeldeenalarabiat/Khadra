using Khadra.Domain.Common;

namespace Khadra.Domain.FinancialDocuments;

/// <summary>
/// Where an issued document stands (payments Phase 5). DERIVED when it is read, never stored: a document
/// row never changes, so its standing is worked out from the rows beside it — a void recorded against
/// it, or a later version of its family.
/// </summary>
public sealed class FinancialDocumentStatus : Enumeration
{
    /// <summary>The latest version of its family, and not voided.</summary>
    public static readonly FinancialDocumentStatus Current = new(1, "Current");

    /// <summary>A later version of the same family exists. Preserved history, readable for good.</summary>
    public static readonly FinancialDocumentStatus Superseded = new(2, "Superseded");

    /// <summary>An administrator voided it; its correction replaced it under a new number.</summary>
    public static readonly FinancialDocumentStatus Voided = new(3, "Voided");

    private FinancialDocumentStatus(int id, string name) : base(id, name)
    {
    }

    /// <summary>
    /// A void wins over a later version: a voided document is always superseded too (its correction is
    /// the next version), and "voided" is what a reader has to be told.
    /// </summary>
    public static FinancialDocumentStatus Of(bool voided, bool superseded) =>
        voided ? Voided : superseded ? Superseded : Current;
}
