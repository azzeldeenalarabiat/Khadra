using System.Net;
using System.Text.Json;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Khadra.Domain.Notifications;
using Khadra.Infrastructure.Configuration;
using Khadra.Infrastructure.Notifications;
using Khadra.Infrastructure.Notifications.Push;
using Khadra.Tests.Support;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Khadra.Tests.Infrastructure;

/// <summary>What a push says, in which language, and how FCM's answers are read.</summary>
public sealed class PushWordingAndFcmTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 7, 0, 0, TimeSpan.Zero); // 10:00 in Amman

    private static NotificationMessageComposer Composer(PaymentMode mode)
    {
        var calendar = Substitute.For<IReportingCalendar>();
        calendar.DayOf(Arg.Any<DateTimeOffset>()).Returns(ci => DateOnly.FromDateTime(ci.Arg<DateTimeOffset>().AddHours(3).DateTime));
        calendar.TimeOfDay(Arg.Any<DateTimeOffset>()).Returns(ci => TimeOnly.FromDateTime(ci.Arg<DateTimeOffset>().AddHours(3).DateTime));
        var payments = Substitute.For<IPaymentProvider>();
        payments.Mode.Returns(mode);
        return new NotificationMessageComposer(calendar, payments, Options.Create(new AppOptions()));
    }

    private static Notification Approved(DateTimeOffset? due) =>
        Notification.Raise(Id.New(), NotificationKind.YourBookingApproved, "Petra Rentals", Now, Id.New(), "KH-24-0007", dueAt: due);

    [Fact]
    public void With_no_payment_provider_an_approval_never_tells_anyone_to_pay()
    {
        var text = Composer(PaymentMode.None).ComposePush(Approved(Now.AddHours(2)), Language.English);

        Assert.Equal("Booking approved", text.Title);
        Assert.DoesNotContain("Pay", text.Body, StringComparison.Ordinal);
        Assert.Contains("KH-24-0007", text.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void When_payment_is_possible_the_approval_states_the_frozen_deadline_in_Amman_time()
    {
        var text = Composer(PaymentMode.Sandbox).ComposePush(Approved(Now.AddHours(2)), Language.English);

        Assert.Contains("deposit due", text.Title, StringComparison.Ordinal);
        Assert.Contains("12:00, 2026-09-23", text.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Arabic_isolates_the_Latin_runs_so_the_reference_does_not_read_backwards()
    {
        var text = Composer(PaymentMode.None).ComposePush(Approved(null), Language.Arabic);

        Assert.Equal("تمت الموافقة على حجزك", text.Title);
        Assert.Contains("⁨KH-24-0007⁩", text.Body, StringComparison.Ordinal);
        Assert.Contains("⁨Petra Rentals⁩", text.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_kind_that_wakes_a_phone_has_its_own_words_in_both_languages()
    {
        foreach (var kind in Enumeration.GetAll<NotificationKind>().Where(k => k.DeliveredOn().Contains(NotificationChannel.Push)))
        {
            var notification = Notification.Raise(Id.New(), kind, "Petra", Now, Id.New(), "KH-1");
            var en = Composer(PaymentMode.None).ComposePush(notification, Language.English);
            var ar = Composer(PaymentMode.None).ComposePush(notification, Language.Arabic);

            Assert.NotEqual("Khadra", en.Title);
            Assert.NotEqual("خضرا", ar.Title);
        }
    }

    [Fact]
    public void An_email_follows_the_chosen_language_and_is_bilingual_when_none_was_chosen()
    {
        var notification = Approved(null);
        var composer = Composer(PaymentMode.None);

        var neither = Users.Customer();
        var both = composer.ComposeEmail(notification, neither);
        Assert.Contains("Booking approved", both.Subject, StringComparison.Ordinal);
        Assert.Contains("تمت الموافقة", both.Subject, StringComparison.Ordinal);

        var arabicReader = Users.Customer();
        arabicReader.ChoosePreferredLanguage(Language.Arabic);
        var arabic = composer.ComposeEmail(notification, arabicReader);
        Assert.DoesNotContain("Booking approved", arabic.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain("Hi ", arabic.TextBody, StringComparison.Ordinal);

        var englishReader = Users.Customer();
        englishReader.ChoosePreferredLanguage(Language.English);
        var english = composer.ComposeEmail(notification, englishReader);
        Assert.Equal("Booking approved", english.Subject);
        Assert.DoesNotContain("مرحباً", english.TextBody, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "UNREGISTERED", true)]
    [InlineData(HttpStatusCode.BadRequest, "INVALID_ARGUMENT", true)]
    [InlineData(HttpStatusCode.Forbidden, "SENDER_ID_MISMATCH", true)]
    [InlineData(HttpStatusCode.TooManyRequests, "QUOTA_EXCEEDED", false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "UNAVAILABLE", false)]
    [InlineData(HttpStatusCode.Unauthorized, "UNAUTHENTICATED", false)]
    public void Only_a_gone_install_or_another_projects_token_is_treated_as_dead(HttpStatusCode code, string status, bool dead)
    {
        Assert.Equal(dead, FcmPushSender.IsDeadToken(code, status));
    }

    [Fact]
    public void The_message_uses_the_apps_channel_the_default_sound_and_the_notification_id_as_tag()
    {
        var envelope = JsonSerializer.Serialize(FcmPushSender.Envelope(new PushMessage(
            "tok", "Title", "Body", new Dictionary<string, string> { ["kind"] = "YourBookingConfirmed" }, "n-1")));
        using var json = JsonDocument.Parse(envelope);
        var message = json.RootElement.GetProperty("message");
        var android = message.GetProperty("android");

        Assert.Equal("tok", message.GetProperty("token").GetString());
        Assert.Equal("Title", message.GetProperty("notification").GetProperty("title").GetString());
        Assert.Equal("YourBookingConfirmed", message.GetProperty("data").GetProperty("kind").GetString());
        Assert.Equal("HIGH", android.GetProperty("priority").GetString());
        Assert.Equal("booking_updates", android.GetProperty("notification").GetProperty("channel_id").GetString());
        Assert.Equal("default", android.GetProperty("notification").GetProperty("sound").GetString());
        Assert.Equal("n-1", android.GetProperty("notification").GetProperty("tag").GetString());
    }

    [Fact]
    public void A_service_account_without_a_key_is_not_a_configuration()
    {
        Assert.False(FcmServiceAccount.TryParse("{\"client_email\":\"x@y\"}", out _));
        Assert.False(FcmServiceAccount.TryParse("not json", out _));
        Assert.True(FcmServiceAccount.TryParse("{\"client_email\":\"x@y\",\"private_key\":\"k\"}", out var account));
        Assert.DoesNotContain("k\"", account.ToString(), StringComparison.Ordinal);
        Assert.False(new PushOptions { Provider = "Fcm", Fcm = new FcmOptions { ProjectId = "khadra-staging" } }.FcmIsComplete);
        Assert.True(new PushOptions { Provider = "None" }.FcmIsComplete);
    }
}
