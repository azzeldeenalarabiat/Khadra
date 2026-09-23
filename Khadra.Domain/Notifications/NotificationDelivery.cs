using Khadra.Domain.Common;

namespace Khadra.Domain.Notifications;

/// <summary>A way of reaching a person who is not looking at the app.</summary>
public sealed class NotificationChannel : Enumeration
{
    /// <summary>A push notification to every phone the person is signed in on.</summary>
    public static readonly NotificationChannel Push = new(1, "Push");

    /// <summary>An email to the person's verified address.</summary>
    public static readonly NotificationChannel Email = new(2, "Email");

    private NotificationChannel(int id, string name) : base(id, name)
    {
    }
}

/// <summary>Where one delivery stands.</summary>
public sealed class DeliveryState : Enumeration
{
    /// <summary>Not yet sent, or waiting to be tried again.</summary>
    public static readonly DeliveryState Pending = new(1, "Pending");

    /// <summary>The push service or mail transport accepted it. Not proof anybody read it.</summary>
    public static readonly DeliveryState Sent = new(2, "Sent");

    /// <summary>There was nobody to send it to: no live phone, no verified address, no provider.</summary>
    public static readonly DeliveryState Skipped = new(3, "Skipped");

    /// <summary>Tried as many times as configuration allows, and never accepted.</summary>
    public static readonly DeliveryState Failed = new(4, "Failed");

    private DeliveryState(int id, string name) : base(id, name)
    {
    }

    public bool IsFinal => this != Pending;
}

/// <summary>
/// One notification, owed on one channel: the outbox row the dispatcher works from.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a row of its own and not a column on <see cref="Notification"/>.</b> A notification is "what
/// a person was told"; this is "did the phone or the mailbox get it", which is a different question
/// with a different lifetime, and one that only customer-facing kinds ever ask. Keeping it apart is
/// what leaves the notifications list, and every dealer row in it, untouched.
/// </para>
/// <para>
/// <b>Written in the same transaction as the notification</b>, by the notifier itself, so a
/// notification can never exist without its delivery or the other way round. That is the outbox:
/// the business change, the notification and the promise to deliver it commit together, and the
/// sending happens afterwards, from here, as many times as it takes.
/// </para>
/// <para>
/// <b>At least once, not exactly once.</b> A process that dies after the push service accepts a
/// message and before this row says so will send it again. The push carries the notification id as
/// its Android tag, so a repeat replaces the first on the phone rather than stacking beside it.
/// </para>
/// </remarks>
public sealed class NotificationDelivery : AggregateRoot
{
    public const int MaxErrorLength = 300;

    public Id NotificationId { get; private set; }
    public NotificationChannel Channel { get; private set; } = null!;
    public DeliveryState State { get; private set; } = null!;

    /// <summary>How many times this has been CLAIMED. A claim counts whether or not the send finished.</summary>
    public int Attempts { get; private set; }

    /// <summary>When the dispatcher may next pick this up. Also the lease on a claimed row.</summary>
    public DateTimeOffset NextAttemptAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>A short reason for the last failure or skip. Never a token, an address or a message body.</summary>
    public string? LastError { get; private set; }

    private NotificationDelivery()
    {
    }

    private NotificationDelivery(Id id) : base(id)
    {
    }

    public static NotificationDelivery Owe(Id notificationId, NotificationChannel channel, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (notificationId.IsEmpty)
            throw new DomainException("A delivery belongs to a notification.");

        return new NotificationDelivery(Id.New())
        {
            NotificationId = notificationId,
            Channel = channel,
            State = DeliveryState.Pending,
            Attempts = 0,
            NextAttemptAt = now,
            CreatedAt = now,
        };
    }

    public void RecordSent(DateTimeOffset now) => Finish(DeliveryState.Sent, null, now);

    public void RecordSkipped(string reason, DateTimeOffset now) => Finish(DeliveryState.Skipped, reason, now);

    /// <summary>
    /// A send that did not go through. Tried again at <paramref name="retryAt"/>, unless this was the
    /// last attempt allowed, in which case it is given up.
    /// </summary>
    public void RecordFailure(string error, int maxAttempts, DateTimeOffset retryAt, DateTimeOffset now)
    {
        if (State.IsFinal)
            return;

        if (Attempts >= maxAttempts)
        {
            Finish(DeliveryState.Failed, error, now);
            return;
        }

        LastError = Trim(error);
        NextAttemptAt = retryAt;
    }

    private void Finish(DeliveryState state, string? reason, DateTimeOffset now)
    {
        // First outcome wins. A second dispatcher that raced past the lease cannot turn a Sent into a
        // Failed, or a Failed back into a Sent.
        if (State.IsFinal)
            return;

        State = state;
        LastError = Trim(reason);
        CompletedAt = now;
    }

    private static string? Trim(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length <= MaxErrorLength ? trimmed : trimmed[..MaxErrorLength];
    }
}
