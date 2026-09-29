using System.Text.RegularExpressions;
using Khadra.Domain.Common;

namespace Khadra.Domain.FinancialDocuments;

/// <summary>A format an issued document is rendered in (payments Phase 6). One today; the design names the column.</summary>
public sealed class RenditionFormat : Enumeration
{
    public static readonly RenditionFormat Pdf = new(1, "Pdf", "application/pdf", "pdf");

    private RenditionFormat(int id, string name, string contentType, string extension) : base(id, name)
    {
        ContentType = contentType;
        Extension = extension;
    }

    public string ContentType { get; }

    /// <summary>The file extension a stored rendition carries, without its dot.</summary>
    public string Extension { get; }
}

/// <summary>
/// What a rendition pictures (payments Phase 6 follow-up; owner, 2026-09-29): the document exactly as it was
/// issued, or — for a voided document — the same document drawn after its void, marked void on every page and
/// naming its correction. A separate axis from the file format: both kinds are PDFs.
/// </summary>
public sealed class RenditionKind : Enumeration
{
    /// <summary>The document exactly as issued. For a voided document, the administrator's alone.</summary>
    public static readonly RenditionKind AsIssued = new(1, "AsIssued", string.Empty);

    /// <summary>
    /// A voided document as its customer is given it: the record as issued, stamped VOID on every page, saying it
    /// is no longer valid and naming the correction that replaced it. Drawn once, after the void, from facts that
    /// never change again — the snapshot, the void and the correction.
    /// </summary>
    public static readonly RenditionKind Voided = new(2, "Voided", "void-");

    private RenditionKind(int id, string name, string keyMarker) : base(id, name) => KeyMarker = keyMarker;

    /// <summary>What a storage key carries after the language, so a stored file says which kind it is.</summary>
    public string KeyMarker { get; }
}

/// <summary>
/// One stored rendering of an issued document in one language (payments Phase 6): a PDF drawn from the
/// document's stored snapshot, once, and kept privately in document storage.
/// </summary>
/// <remarks>
/// <para>
/// It is a REPRESENTATION of the record, never the record (owner, 2026-09-27): the document row and its
/// snapshot are the official record, and a rendition proves only what bytes were stored
/// (<see cref="ContentSha256"/>) and which snapshot they were drawn from (<see cref="SnapshotSha256"/>, the
/// document's own hash when it was drawn).
/// </para>
/// <para>
/// Append-only, like the document: one per document, language, format, kind and template version, never
/// replaced. A new template renders documents from then on beside the old renditions rather than over them, so
/// what a customer downloaded stays what the platform holds. A void never changes a rendition either (owner,
/// 2026-09-29): the as-issued PDF stays exactly as it was, for the administrator alone, and a second rendition —
/// the <see cref="RenditionKind.Voided"/> copy — is drawn after the void for the customer. The void is unique per
/// document and final, and the correction is the family's next version, so what that copy pictures never changes.
/// </para>
/// </remarks>
public sealed partial class FinancialDocumentRendition : AggregateRoot, IAppendOnly
{
    public const int MaxStorageKeyLength = 300;
    public const int MaxRendererVersionLength = 64;

    public Id DocumentId { get; private set; }

    public Language Language { get; private set; } = null!;

    public RenditionFormat Format { get; private set; } = null!;

    /// <summary>Whether it pictures the document as issued, or as voided.</summary>
    public RenditionKind Kind { get; private set; } = null!;

    /// <summary>The layout's version (<c>DocumentPrintLayout.TemplateVersion</c>) the bytes were drawn with.</summary>
    public int TemplateVersion { get; private set; }

    /// <summary>What drew the bytes: the library and its version.</summary>
    public string RendererVersion { get; private set; } = null!;

    /// <summary>Where the bytes are, in private document storage. The row is the only pointer to them.</summary>
    public string StorageKey { get; private set; } = null!;

    /// <summary>SHA-256 (lowercase hex) of the stored bytes.</summary>
    public string ContentSha256 { get; private set; } = null!;

    public long SizeBytes { get; private set; }

    /// <summary>The document's own snapshot hash when the bytes were drawn: which record they represent.</summary>
    public string SnapshotSha256 { get; private set; } = null!;

    public DateTimeOffset RenderedAt { get; private set; }

    private FinancialDocumentRendition()
    {
    }

    private FinancialDocumentRendition(Id id) : base(id)
    {
    }

    /// <summary>
    /// Records bytes already stored for <paramref name="document"/>. Every argument is the system's own, so a
    /// wrong one is a defect, not a customer's mistake: it throws.
    /// </summary>
    public static FinancialDocumentRendition Record(
        FinancialDocument document,
        Language language,
        RenditionFormat format,
        RenditionKind kind,
        int templateVersion,
        string rendererVersion,
        string storageKey,
        string contentSha256,
        long sizeBytes,
        DateTimeOffset renderedAt)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(kind);
        if (templateVersion < 1)
            throw new DomainException("A rendition's template version starts at 1.");
        if (string.IsNullOrWhiteSpace(rendererVersion) || rendererVersion.Length > MaxRendererVersionLength)
            throw new DomainException("A rendition names what drew it, in at most 64 characters.");
        if (string.IsNullOrWhiteSpace(storageKey) || storageKey.Length > MaxStorageKeyLength)
            throw new DomainException("A rendition points at its stored bytes.");
        if (contentSha256 is null || !Sha256Hex().IsMatch(contentSha256))
            throw new DomainException("A rendition's hash is SHA-256 in lowercase hexadecimal.");
        if (sizeBytes <= 0)
            throw new DomainException("A rendition has bytes.");

        return new FinancialDocumentRendition(Id.New())
        {
            DocumentId = document.Id,
            Language = language,
            Format = format,
            Kind = kind,
            TemplateVersion = templateVersion,
            RendererVersion = rendererVersion,
            StorageKey = storageKey,
            ContentSha256 = contentSha256,
            SizeBytes = sizeBytes,
            SnapshotSha256 = document.ContentSha256,
            RenderedAt = renderedAt,
        };
    }

    /// <summary>
    /// The private storage key for one attempt at rendering: a fresh name each time, so an attempt that stored
    /// bytes and never recorded them (a crash between the two) leaves an orphan the next attempt steps
    /// around, rather than a key it can never write again. A voided copy's key says so (<c>…-en-void-….pdf</c>).
    /// </summary>
    public static string NewStorageKey(Id documentId, Language language, RenditionFormat format, RenditionKind kind, int templateVersion)
    {
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(kind);
        return $"financial-documents/{documentId.Value:D}/v{templateVersion}-{language.Name}-{kind.KeyMarker}{Guid.CreateVersion7():N}.{format.Extension}";
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Hex();
}
