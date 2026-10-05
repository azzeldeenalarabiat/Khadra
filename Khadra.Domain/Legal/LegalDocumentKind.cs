using Khadra.Domain.Common;

namespace Khadra.Domain.Legal;

/// <summary>
/// Which legal text a version belongs to (Wave 2 G1).
/// </summary>
/// <remarks>
/// Add-only: a member is never renamed or removed, because every published version and, from Wave 4, every consent
/// names one for good. The database refuses a name no build knows (a CHECK built from this list), so adding a member
/// changes the model and needs a migration.
/// </remarks>
public sealed class LegalDocumentKind : Enumeration
{
    public static readonly LegalDocumentKind Terms = new(1, "Terms", "terms");

    public static readonly LegalDocumentKind Privacy = new(2, "Privacy", "privacy");

    private LegalDocumentKind(int id, string name, string slug) : base(id, name)
    {
        Slug = slug;
    }

    /// <summary>The path segment the text is served and published under: <c>/en/terms</c>, <c>/legal-documents/terms/current</c>.</summary>
    public string Slug { get; }

    /// <summary>The kind a path segment names, or null for one this build does not know.</summary>
    public static LegalDocumentKind? FromSlug(string? slug) =>
        GetAll<LegalDocumentKind>().FirstOrDefault(kind => string.Equals(kind.Slug, slug, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The kind an administrator's request names: by its name (<c>Terms</c>), as the console sends it, or else by its
    /// slug. Asked by name first, so a later kind whose slug is not its lower-cased name is still found (advisor's review
    /// of Wave 2). Null for one this build does not know.
    /// </summary>
    public static LegalDocumentKind? FromNameOrSlug(string? value) =>
        GetAll<LegalDocumentKind>().FirstOrDefault(kind => string.Equals(kind.Name, value, StringComparison.OrdinalIgnoreCase))
        ?? FromSlug(value);
}
