namespace Khadra.Application.Common.Ports;

/// <summary>
/// What a stored document will be served as, read from its storage key.
///
/// One function, because there were three answers to this question and nothing kept them in step:
/// the download endpoint decided the response's content type, the vehicle-image endpoint decided its
/// own, and the console decided what to tell the Admin they were about to open (it said ".jpg" for
/// everything, including the PDFs the seeder writes). A reviewer checking a licence is entitled to
/// have those be the same answer.
///
/// The key's extension is authoritative and always present: <c>IDocumentStorage</c> generates every
/// key itself and only ever appends an extension from the accepted list, so nothing here is
/// guessing at an uploader's filename.
/// </summary>
public static class DocumentContentTypes
{
    public const string Fallback = "image/jpeg";

    /// <summary>The MIME type for a storage key, matching what the document endpoint returns.</summary>
    public static string ForStorageKey(string? storageKey) =>
        Path.GetExtension(storageKey ?? string.Empty).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            _ => Fallback
        };
}
