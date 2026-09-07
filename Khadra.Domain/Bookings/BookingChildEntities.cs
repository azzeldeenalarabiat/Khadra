using System.Security.Cryptography;
using CSharpFunctionalExtensions;
using Khadra.Domain.Common;
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
