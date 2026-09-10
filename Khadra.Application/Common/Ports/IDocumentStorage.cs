namespace Khadra.Application.Common.Ports;

/// <summary>Where a stored file lives, and what it is. Never a URL.</summary>
public sealed record StoredDocument(string StorageKey, string ContentType, long SizeBytes);

/// <summary>
/// Something is already stored at that key, and storage refused to replace it.
/// </summary>
/// <remarks>
/// Not a failure of the upload so much as the answer to a question the caller did not mean to ask.
/// A presigned ticket commits to a key before the bytes exist and is meant to be spent ONCE; letting
/// a second upload overwrite the first would mean the person who raised a dispute could swap the
/// evidence after the other party and an administrator had read it, leaving the same key on the same
/// record with different content inside.
///
/// So the second write is refused, and the API answers 409 rather than 500. For a client retrying
/// after a lost response that is the good news: the bytes are already there, and the next step is the
/// confirmation call, not another upload.
/// </remarks>
public sealed class DocumentAlreadyExistsException(string storageKey)
    : Exception($"A document is already stored at '{storageKey}'.")
{
    public string StorageKey { get; } = storageKey;
}

/// <summary>
/// Access-controlled storage for sensitive documents (spec 7).
///
/// The contract deliberately returns a KEY, not a URL. Identity papers and dealer commercial
/// documents must never be reachable by guessing an address: a caller proves it may see a file by
/// asking this port, which is what makes "visible to a dealer only once that customer sends them a
/// booking request" enforceable later. The local implementation writes outside the web root; swapping
/// it for S3 or MinIO is a new class and no change above this line.
/// </summary>
public interface IDocumentStorage
{
    /// <param name="scope">
    /// A coarse folder such as "customers/{userId}" or "dealers/{dealerId}". Keys are generated, not
    /// derived from the uploaded file name, so a caller cannot steer where bytes land.
    /// </param>
    Task<StoredDocument> SaveAsync(
        string scope,
        string fileName,
        string contentType,
        Stream content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes AT a key that was decided earlier. The presigned flow needs this: the ticket commits to
    /// a key before the bytes exist, and SaveAsync generating its own would leave the file somewhere
    /// the confirmation step will never look.
    /// </summary>
    Task<StoredDocument> SaveAtAsync(
        string storageKey,
        string contentType,
        Stream content,
        CancellationToken cancellationToken = default);

    Task<Stream?> OpenAsync(string storageKey, CancellationToken cancellationToken = default);

    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
}

public sealed record SignedDocumentLink(string Url, DateTimeOffset ExpiresAt);

/// <summary>
/// Mints the short-lived links spec 7 asks for, and checks them again on the way back in.
///
/// Separate from IDocumentStorage because the two answer different questions: one is "where are the
/// bytes", the other is "is this particular request allowed to have them, right now". A link that
/// never expired would be a public URL wearing a disguise.
/// </summary>
public interface IDocumentLinkSigner
{
    SignedDocumentLink Sign(string storageKey, DateTimeOffset now);

    bool IsValid(string storageKey, long expiresAtUnixSeconds, string signature, DateTimeOffset now);

    /// <summary>Recovers the storage key from the opaque token in a signed link.</summary>
    bool TryDecodeToken(string token, out string storageKey);
}

/// <summary>What the platform will accept as an uploaded document.</summary>
public interface IDocumentPolicySettings
{
    long MaximumSizeBytes { get; }

    IReadOnlyCollection<string> AllowedContentTypes { get; }

    /// <summary>How long a signed view link stays usable.</summary>
    TimeSpan LinkLifetime { get; }
}
