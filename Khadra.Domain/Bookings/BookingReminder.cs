using Khadra.Domain.Common;

namespace Khadra.Domain.Bookings;

/// <summary>What a reminder is about.</summary>
public sealed class ReminderKind : Enumeration
{
    /// <summary>The deposit is still unpaid and its deadline is near.</summary>
    public static readonly ReminderKind Payment = new(1, "Payment");

    /// <summary>The rental starts soon: go and collect the car.</summary>
    public static readonly ReminderKind Pickup = new(2, "Pickup");

    /// <summary>The rental ends soon: bring the car back.</summary>
    public static readonly ReminderKind Return = new(3, "Return");

    private ReminderKind(int id, string name) : base(id, name)
    {
    }
}

/// <summary>
/// The record that one reminder was sent: the booking, what it was about, and the moment it was about.
/// </summary>
/// <remarks>
/// <para>
/// <b>It exists to make reminders idempotent.</b> The sweep runs every minute, possibly in two
/// processes at once during a deploy, and it asks the same question every time: which bookings are
/// inside a reminder window. This row is how it knows one has been sent. The database holds a UNIQUE
/// key on (booking, kind, anchor), and a reminder is written in the SAME transaction as the
/// notification it raises — so a second sweep, however it races, can never send a second one.
/// </para>
/// <para>
/// <b>Why the anchor is part of the key.</b> The anchor is the booking time the reminder is about —
/// the payment deadline, the rental start, the rental end. If that time legitimately moves (an
/// extension moves the end), the new time earns its own reminder; if configuration changes the lead
/// time, the anchor has not moved and nothing is sent twice.
/// </para>
/// <para>
/// A separate aggregate rather than a child of <see cref="Booking"/>: writing one must not dirty the
/// booking's concurrency token, or a reminder landing at the moment a dealer records a pickup would
/// make the dealer's request fail for no reason of theirs.
/// </para>
/// </remarks>
public sealed class BookingReminder : AggregateRoot
{
    public Id BookingId { get; private set; }
    public ReminderKind Kind { get; private set; } = null!;

    /// <summary>The booking time this reminder is about. Frozen: what the customer was told.</summary>
    public DateTimeOffset AnchorAt { get; private set; }

    public DateTimeOffset SentAt { get; private set; }

    private BookingReminder()
    {
    }

    private BookingReminder(Id id) : base(id)
    {
    }

    public static BookingReminder Record(Id bookingId, ReminderKind kind, DateTimeOffset anchorAt, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(kind);
        if (bookingId.IsEmpty)
            throw new DomainException("A reminder belongs to a booking.");
        if (anchorAt <= now)
            throw new DomainException("A reminder is only ever sent before the moment it is about.");

        return new BookingReminder(Id.New())
        {
            BookingId = bookingId,
            Kind = kind,
            AnchorAt = anchorAt,
            SentAt = now,
        };
    }
}
