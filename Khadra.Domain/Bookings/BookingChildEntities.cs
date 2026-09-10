using System.Security.Cryptography;
using CSharpFunctionalExtensions;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Entity = Khadra.Domain.Common.Entity;
using ValueObject = Khadra.Domain.Common.ValueObject;

namespace Khadra.Domain.Bookings;

// Short human-quotable code. Customers read it out on the phone; a UUID is unusable for that.
// Ambiguous characters (0/O, 1/I) are excluded on purpose.
public sealed class BookingReference : ValueObject
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 8;

    public string Value { get; }

    private BookingReference(string value)
    {
        Value = value;
    }

    /// <summary>
    /// A value already in the database, taken as-is.
    /// </summary>
    /// <remarks>
    /// Reading a row is not the moment to re-litigate whether it should have been allowed in. The EF
    /// converters used to rebuild these through <c>Create(...).Value</c>, and <c>.Value</c> on a
    /// failed result THROWS — so the day a rule is tightened in a way some stored row no longer
    /// satisfies, that row stops being readable at all. Not a validation error the caller could
    /// handle: an exception on load, for every query that touches the aggregate.
    ///
    /// Writes still go through <see cref="Create"/>, which is where the rule belongs.
    /// </remarks>
    public static BookingReference FromPersisted(string value) => new(value);

    public static BookingReference New()
    {
        var characters = new char[CodeLength];
        for (var index = 0; index < CodeLength; index++)
            characters[index] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];

        return new BookingReference($"KH-{new string(characters)}");
    }

    public static Result<BookingReference, Error> Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Error.Validation("booking.invalid_reference", "The booking reference is not valid.");

        var normalized = raw.Trim().ToUpperInvariant();
        if (normalized.Length is < 4 or > 20)
            return Error.Validation("booking.invalid_reference", "The booking reference is not valid.");

        return new BookingReference(normalized);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}

// Spec 5.4: handover photos are OPT-IN and the platform is a neutral record-keeper. It never
// arbitrates condition from these; that stays the dealer's own off-platform process.
public sealed class HandoverRecord : Entity
{
    // Not readonly: EF materialises this collection by assigning the field when reading a record.
    private List<string> _photoStorageKeys = [];

    public Id BookingId { get; private set; }
    public HandoverType Type { get; private set; } = null!;
    public BookingParty RecordedBy { get; private set; } = null!;
    public Id RecordedByUserId { get; private set; }
    public int? OdometerKm { get; private set; }
    public decimal? FuelLevel { get; private set; }
    public string? Notes { get; private set; }
    // Cash the dealer acknowledges collecting at handover (the 80% balance, and any cash damage
    // deposit). Recorded as a fact, not as a platform payment: this money never touches the platform.
    public Money? CashCollected { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }

    public IReadOnlyCollection<string> PhotoStorageKeys => _photoStorageKeys.AsReadOnly();

    private HandoverRecord()
    {
    }

    private HandoverRecord(Id id) : base(id)
    {
    }

    internal static Result<HandoverRecord, Error> Create(
        Id bookingId,
        HandoverType type,
        BookingParty recordedBy,
        Id recordedByUserId,
        DateTimeOffset now,
        IEnumerable<string>? photoStorageKeys = null,
        int? odometerKm = null,
        decimal? fuelLevel = null,
        string? notes = null,
        Money? cashCollected = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(recordedBy);
        if (bookingId.IsEmpty)
            throw new DomainException("A handover record requires a booking.");

        if (odometerKm is < 0)
            return Error.Validation("handover.invalid_odometer", "The odometer reading cannot be negative.");
        if (fuelLevel is < 0m or > 1m)
            return Error.Validation("handover.invalid_fuel", "The fuel level must be between 0 and 1.");

        var record = new HandoverRecord(Id.New())
        {
            BookingId = bookingId,
            Type = type,
            RecordedBy = recordedBy,
            RecordedByUserId = recordedByUserId,
            OdometerKm = odometerKm,
            FuelLevel = fuelLevel,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            CashCollected = cashCollected,
            RecordedAt = now
        };

        foreach (var key in photoStorageKeys ?? [])
        {
            if (!string.IsNullOrWhiteSpace(key))
                record._photoStorageKeys.Add(key.Trim());
        }

        return record;
    }
}

// Append-only trail of every status change. Domain events are dispatched in-process and are not
// persisted yet, so without this a dispute has no evidence of who did what and when.
public sealed class BookingStatusChange : Entity
{
    public Id BookingId { get; private set; }
    public BookingStatus? From { get; private set; }
    public BookingStatus To { get; private set; } = null!;
    public BookingParty ActorParty { get; private set; } = null!;
    public Id? ActorUserId { get; private set; }

    /// <summary>
    /// The closed-set code behind this change, where one exists: a rejection reason, a cancellation
    /// reason. Null for transitions nobody chose a reason for.
    /// </summary>
    /// <remarks>
    /// Stored beside <see cref="Reason"/> rather than folded into it. Until 2026-09-08 the rejection
    /// code was composed into an English sentence on the way in, which put untranslatable prose on a
    /// permanent record: an Arabic-speaking customer read English on their own booking and no client
    /// could do anything about it. The code is the platform's word for what happened; the sentence
    /// is chosen by whoever reads it.
    /// </remarks>
    public string? ReasonCode { get; private set; }

    /// <summary>What the actor typed, if anything. Their own words, in their own language.</summary>
    public string? Reason { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    private BookingStatusChange()
    {
    }

    private BookingStatusChange(Id id) : base(id)
    {
    }

    internal static BookingStatusChange Record(
        Id bookingId,
        BookingStatus? from,
        BookingStatus to,
        BookingParty actorParty,
        Id? actorUserId,
        string? reason,
        DateTimeOffset now,
        string? reasonCode = null) =>
        new(Id.New())
        {
            BookingId = bookingId,
            From = from,
            To = to,
            ActorParty = actorParty,
            ActorUserId = actorUserId,
            ReasonCode = string.IsNullOrWhiteSpace(reasonCode) ? null : reasonCode.Trim(),
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            OccurredAt = now
        };
}

/// <summary>
/// A record that this dealership LOOKED at one of the renter's documents (spec 5.1).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is not a verification, and the wording is load-bearing.</b> It says that the gallery
/// handling this booking opened the file the renter uploaded and satisfied itself. It makes no claim
/// that the document is authentic, current, or checked against any register — Khadra does not do
/// that, and pre-launch item 27 is where whether anyone ever will gets decided. The platform's own
/// <c>CustomerDocument.Status</c> is a separate, still-unused field and this deliberately does not
/// touch it.
/// </para>
/// <para>
/// It hangs off the BOOKING, not off the document, because that is what it is a fact about. Two
/// galleries renting to the same customer each check the licence for themselves, and neither one's
/// look says anything about the other's.
/// </para>
/// <para>
/// <b><see cref="DocumentUploadedAt"/> is part of the identity of the thing reviewed, not decoration.</b>
/// <c>CustomerDocument.Replace</c> keeps the row's id and swaps the file underneath it — that is what
/// makes a re-photographed licence a replacement rather than a pile of attempts. A review keyed on
/// the document id alone would therefore survive the swap, and the console would show "reviewed by
/// dealer" over a file nobody at the dealership has ever seen, at the moment the car is handed over.
/// That is item 63's failure reopened by the feature meant to close it. The upload instant is the
/// version, and a new photograph needs a new look.
/// </para>
/// </remarks>
public sealed class RenterDocumentReview : Entity
{
    public const int MaxReviewerNameLength = 200;

    public Id BookingId { get; private set; }

    /// <summary>The document, by id only: it belongs to IdentityAccess and is never navigated to.</summary>
    public Id DocumentId { get; private set; }

    /// <summary>Which paper it was, so the record still reads if the document row is ever gone.</summary>
    public CustomerDocumentType DocumentType { get; private set; } = null!;

    /// <summary>Which UPLOAD was reviewed. See the remarks: this is half the key.</summary>
    public DateTimeOffset DocumentUploadedAt { get; private set; }

    public Id ReviewedByUserId { get; private set; }

    /// <summary>
    /// The reviewer's name as it was at the time.
    /// </summary>
    /// <remarks>
    /// Snapshotted rather than looked up, the same rule <c>AuditEntry.ActorName</c> follows: "reviewed
    /// by Layla Haddad" has to keep reading correctly after she leaves the dealership and her employee
    /// row is deactivated, which is exactly when someone goes looking for who checked the licence.
    /// </remarks>
    public string ReviewedByName { get; private set; } = null!;

    public DateTimeOffset ReviewedAt { get; private set; }

    private RenterDocumentReview()
    {
    }

    private RenterDocumentReview(Id id) : base(id)
    {
    }

    internal static RenterDocumentReview Record(
        Id bookingId,
        Id documentId,
        CustomerDocumentType documentType,
        DateTimeOffset documentUploadedAt,
        Id reviewedByUserId,
        string reviewedByName,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(documentType);
        if (bookingId.IsEmpty || documentId.IsEmpty)
            throw new DomainException("A document review requires a booking and a document.");
        if (reviewedByUserId.IsEmpty)
            throw new DomainException("A document review requires the person who made it.");
        if (string.IsNullOrWhiteSpace(reviewedByName))
            throw new DomainException("A document review requires the reviewer's name.");

        return new RenterDocumentReview(Id.New())
        {
            BookingId = bookingId,
            DocumentId = documentId,
            DocumentType = documentType,
            DocumentUploadedAt = documentUploadedAt,
            ReviewedByUserId = reviewedByUserId,
            ReviewedByName = reviewedByName.Trim()[..Math.Min(reviewedByName.Trim().Length, MaxReviewerNameLength)],
            ReviewedAt = now
        };
    }
}
