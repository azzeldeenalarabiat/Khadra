using Khadra.Domain.Common;
using Entity = Khadra.Domain.Common.Entity;

namespace Khadra.Domain.IdentityAccess;

// Spec 5.1: the licence is photographed front and back, and identity is a national ID for locals or a
// passport for foreign renters. Four types rather than the two URL columns of the v3.0 data model,
// because a single column cannot hold both sides of a licence.
public sealed class CustomerDocumentType : Enumeration
{
    public static readonly CustomerDocumentType DrivingLicenceFront = new(1, "DrivingLicenceFront");
    public static readonly CustomerDocumentType DrivingLicenceBack = new(2, "DrivingLicenceBack");
    public static readonly CustomerDocumentType NationalId = new(3, "NationalId");
    public static readonly CustomerDocumentType Passport = new(4, "Passport");

    private CustomerDocumentType(int id, string name) : base(id, name)
    {
    }

    public bool IsLicence => this == DrivingLicenceFront || this == DrivingLicenceBack;

    public bool IsIdentity => this == NationalId || this == Passport;
}

// Spec 5.1 asks for a legibility and expiry check before a customer can book, but names no mechanism
// (manual review, OCR, a third-party service). The status is modelled now so the decision has
// somewhere to land; nothing in this pass moves a document out of PendingReview.
public sealed class CustomerDocumentStatus : Enumeration
{
    public static readonly CustomerDocumentStatus PendingReview = new(1, "PendingReview");
    public static readonly CustomerDocumentStatus Verified = new(2, "Verified");
    public static readonly CustomerDocumentStatus Rejected = new(3, "Rejected");

    private CustomerDocumentStatus(int id, string name) : base(id, name)
    {
    }
}

/// <summary>
/// A customer's identity paperwork.
///
/// Only the storage KEY lives here, never a URL. Spec 7 is explicit that these are sensitive personal
/// data: they sit in access-controlled storage and are reached only through this platform's own
/// authenticated endpoints, never by an address anyone can hold. Putting a URL on the record is what
/// would make that impossible to enforce later.
///
/// Two callers may reach a file, and they are served DIFFERENTLY on purpose:
/// <list type="bullet">
/// <item>
/// The customer themselves, through a short-lived signed link (<c>GET /customers/me/documents/{id}/link</c>).
/// Their right to their own paperwork is stable for the whole session, so authorising once and
/// delivering later costs nothing.
/// </item>
/// <item>
/// A dealer holding a LIVE booking with them, through <c>GET /bookings/{id}/renter-documents/{documentId}</c>,
/// which streams the bytes and re-checks the relationship on every request. That right is not stable
/// -- it ends the instant <c>Booking.IsLive</c> does -- so a signed link would be a grant that
/// outlived the rule that issued it, and its token would put the storage key in a gallery's browser.
/// Deliberately not the signer; see pre-launch items 14 and 63 before "harmonising" the two.
/// </item>
/// </list>
/// </summary>
public sealed class CustomerDocument : Entity
{
    public const int MaxStorageKeyLength = 500;

    public Id UserId { get; private set; }
    public CustomerDocumentType Type { get; private set; } = null!;
    public CustomerDocumentStatus Status { get; private set; } = null!;
    public string StorageKey { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public long SizeBytes { get; private set; }
    public DateTimeOffset UploadedAt { get; private set; }
    public string? ReviewNote { get; private set; }

    private CustomerDocument()
    {
    }

    private CustomerDocument(Id id) : base(id)
    {
    }

    internal static CustomerDocument Attach(
        Id userId,
        CustomerDocumentType type,
        string storageKey,
        string contentType,
        long sizeBytes,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (userId.IsEmpty)
            throw new DomainException("A document requires a user.");
        if (string.IsNullOrWhiteSpace(storageKey) || storageKey.Length > MaxStorageKeyLength)
            throw new DomainException("A document storage key is required.");
        if (string.IsNullOrWhiteSpace(contentType))
            throw new DomainException("A document content type is required.");
        if (sizeBytes <= 0)
            throw new DomainException("A document must have content.");

        return new CustomerDocument(Id.New())
        {
            UserId = userId,
            Type = type,
            Status = CustomerDocumentStatus.PendingReview,
            StorageKey = storageKey.Trim(),
            ContentType = contentType.Trim(),
            SizeBytes = sizeBytes,
            UploadedAt = now
        };
    }

    /// <summary>
    /// The customer re-photographed this document. The row is the SLOT ("your licence front"), so it
    /// is updated in place rather than replaced: the id stays stable for anyone holding it, no orphan
    /// row is created, and review starts again from scratch because the file is new.
    /// Returns the key of the file this supersedes so the caller can delete it after committing.
    /// </summary>
    internal string Replace(string storageKey, string contentType, long sizeBytes, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || storageKey.Length > MaxStorageKeyLength)
            throw new DomainException("A document storage key is required.");
        if (string.IsNullOrWhiteSpace(contentType))
            throw new DomainException("A document content type is required.");
        if (sizeBytes <= 0)
            throw new DomainException("A document must have content.");

        var previous = StorageKey;
        StorageKey = storageKey.Trim();
        ContentType = contentType.Trim();
        SizeBytes = sizeBytes;
        Status = CustomerDocumentStatus.PendingReview;
        ReviewNote = null;
        UploadedAt = now;
        return previous;
    }

    internal void MarkVerified()
    {
        Status = CustomerDocumentStatus.Verified;
        ReviewNote = null;
    }

    internal void MarkRejected(string reason)
    {
        Status = CustomerDocumentStatus.Rejected;
        ReviewNote = reason;
    }
}
