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

    /// <summary>
    /// An office that left the platform is a stand-in code (pre-launch item 103): the Arabic push words it, never the
    /// English phrase the row also carries for installed apps.
    /// </summary>
    [Fact]
    public void A_rental_office_stand_in_is_worded_in_the_push_language()
    {
        var expired = Notification.RaiseByStandIn(
            Id.New(), NotificationKind.YourBookingApproved, NotificationStandIn.RentalOffice, Now, Id.New(), "KH-24-0007");

        var arabic = Composer(PaymentMode.None).ComposePush(expired, Language.Arabic);
        var english = Composer(PaymentMode.None).ComposePush(expired, Language.English);

        Assert.Contains("مكتب التأجير", arabic.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("The rental office", arabic.Body, StringComparison.Ordinal);
        Assert.Contains("The rental office", english.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// One kind serves a deposit and a payment in full (owner, 2026-09-25), and the notification row
    /// does not record which, so neither the confirmation nor the refund may say "deposit".
    /// </summary>
    [Theory]
    [InlineData("YourBookingConfirmed")]
    [InlineData("YourDepositRefunded")]
    [InlineData("YourPartialRefundSettled")]
    public void A_confirmation_and_a_refund_speak_of_the_payment_not_the_deposit(string kindName)
    {
        var kind = Enumeration.FromName<NotificationKind>(kindName);
        var notification = Notification.Raise(Id.New(), kind, "Petra", Now, Id.New(), "KH-1");

        var en = Composer(PaymentMode.Sandbox).ComposePush(notification, Language.English);
        var ar = Composer(PaymentMode.Sandbox).ComposePush(notification, Language.Arabic);

        Assert.DoesNotContain("deposit", en.Title + en.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("payment", en.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("العربون", ar.Title + ar.Body, StringComparison.Ordinal);
        Assert.Contains("دفعتك", ar.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Phase 3: while part of a payment is still out or held, the customer is never told "your payment
    /// has been refunded" — that sentence is kept for the moment all of it is back.
    /// </summary>
    [Fact]
    public void A_partial_refund_says_part_of_the_payment_in_both_languages()
    {
        var notification = Notification.Raise(Id.New(), NotificationKind.YourPartialRefundSettled, "Petra", Now, Id.New(), "KH-1");

        var en = Composer(PaymentMode.Sandbox).ComposePush(notification, Language.English);
        var ar = Composer(PaymentMode.Sandbox).ComposePush(notification, Language.Arabic);

        Assert.Contains("part of your payment", en.Body, StringComparison.Ordinal);
        Assert.Contains("جزء من دفعتك", ar.Body, StringComparison.Ordinal);
        Assert.Contains("KH-1", en.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A refund the platform made (an administrator's dispute decision or cancellation) is Khadra's, and names no rental
    /// office, which did not make it (Wave 2 C6; E2E F48). In both languages, and for both refund kinds.
    /// </summary>
    [Theory]
    [InlineData("YourDepositRefunded", "Khadra has refunded your payment", "أعادت خضرا دفعتك")]
    [InlineData("YourPartialRefundSettled", "Khadra has refunded part of your payment", "أعادت خضرا جزءًا من دفعتك")]
    public void A_refund_the_platform_made_is_announced_as_khadras(string kindName, string english, string arabic)
    {
        var kind = Enumeration.FromName<NotificationKind>(kindName);
        var platform = Notification.Raise(Id.New(), kind, Notification.PlatformActorName, Now, Id.New(), "KH-1");
        Assert.True(platform.IsFromPlatform);

        var en = Composer(PaymentMode.Sandbox).ComposePush(platform, Language.English);
        var ar = Composer(PaymentMode.Sandbox).ComposePush(platform, Language.Arabic);

        Assert.StartsWith(english, en.Body, StringComparison.Ordinal);
        Assert.StartsWith(arabic, ar.Body, StringComparison.Ordinal);
        Assert.Contains("KH-1", en.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(" with ", en.Body, StringComparison.Ordinal);
        var email = Composer(PaymentMode.Sandbox).ComposeEmail(platform, Users.Customer());
        Assert.Contains(english, email.TextBody, StringComparison.Ordinal);
    }

    /// <summary>Every other refund follows the office's booking, and keeps the office's name.</summary>
    [Fact]
    public void A_refund_the_offices_booking_made_keeps_the_offices_name()
    {
        var office = Notification.Raise(Id.New(), NotificationKind.YourDepositRefunded, "Petra", Now, Id.New(), "KH-1");
        Assert.False(office.IsFromPlatform);

        var en = Composer(PaymentMode.Sandbox).ComposePush(office, Language.English);

        Assert.StartsWith("Booking KH-1 with Petra", en.Body, StringComparison.Ordinal);
    }

    /// <summary>A person whose name happens to be the platform's is still a person: only no actor id and the name say platform.</summary>
    [Fact]
    public void Only_the_platform_with_no_person_behind_it_is_from_the_platform()
    {
        var person = Notification.Raise(Id.New(), NotificationKind.YourDepositRefunded, Notification.PlatformActorName, Now, actorUserId: Id.New());

        Assert.False(person.IsFromPlatform);
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
    // A malformed message answers the same code as a bad token; it must never revoke phones.
    [InlineData(HttpStatusCode.BadRequest, "INVALID_ARGUMENT", false)]
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
