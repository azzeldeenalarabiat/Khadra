using Khadra.Domain.Common;
using Khadra.Domain.Notifications;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.Notifications;

// A notification is a fact told to one person, and whether they have seen it. The edges that matter:
// it belongs to exactly one recipient, reading it twice does not rewrite when it was first seen, and
// nothing about the subject's identity is stored on it.
public sealed class NotificationTests
{
    private static readonly Id Recipient = Id.New();
    private static readonly Id Actor = Id.New();

    [Fact]
    public void A_notification_starts_unread()
    {
        var notification = Notification.Raise(
            Recipient, NotificationKind.BookingApproved, "Ahmad Zaid", Build.Now);

        Assert.False(notification.IsRead);
        Assert.Null(notification.ReadAt);
        Assert.Equal(Recipient, notification.RecipientUserId);
        Assert.Equal(Build.Now, notification.OccurredAt);
    }

    [Fact]
    public void Reading_it_twice_keeps_the_first_moment()
    {
        var notification = Notification.Raise(
            Recipient, NotificationKind.BookingApproved, "Ahmad Zaid", Build.Now);

        var first = Build.Now.AddMinutes(5);
        notification.MarkRead(first);
        notification.MarkRead(first.AddHours(2));

        Assert.True(notification.IsRead);
        Assert.Equal(first, notification.ReadAt);
    }

    [Fact]
    public void It_belongs_to_its_recipient_and_to_nobody_else()
    {
        var notification = Notification.Raise(
            Recipient, NotificationKind.BookingApproved, "Ahmad Zaid", Build.Now, actorUserId: Actor);

        Assert.True(notification.BelongsTo(Recipient));
        Assert.False(notification.BelongsTo(Actor));
        Assert.False(notification.BelongsTo(Id.New()));
    }

    [Fact]
    public void The_actor_name_is_snapshotted_so_the_line_survives_a_rename()
    {
        var notification = Notification.Raise(
            Recipient,
            NotificationKind.BookingApproved,
            "  Ahmad Zaid  ",
            Build.Now,
            subjectReference: "KR-1042");

        Assert.Equal("Ahmad Zaid", notification.ActorName);
        Assert.Equal("KR-1042", notification.SubjectReference);
    }

    [Fact]
    public void A_notification_requires_a_recipient_and_an_actor_name()
    {
        Assert.Throws<DomainException>(() =>
            Notification.Raise(Id.Empty, NotificationKind.BookingApproved, "Ahmad", Build.Now));

        Assert.Throws<DomainException>(() =>
            Notification.Raise(Recipient, NotificationKind.BookingApproved, "   ", Build.Now));
    }

    [Fact]
    public void Deleting_is_soft_and_idempotent()
    {
        var notification = Notification.Raise(
            Recipient, NotificationKind.BookingApproved, "Ahmad Zaid", Build.Now);

        notification.Delete(Build.Now);
        var first = notification.DeletedAt;
        notification.Delete(Build.Now.AddDays(1));

        Assert.True(notification.IsDeleted);
        Assert.Equal(first, notification.DeletedAt);
    }
}
