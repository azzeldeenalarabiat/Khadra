using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.Notifications;
using Khadra.Infrastructure.Configuration;
using Khadra.Infrastructure.Notifications;
using Khadra.Tests.Support;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Khadra.Tests.Infrastructure;

/// <summary>
/// What an office's email says, and where it leads (Fix & Polish Wave 3, C5), and the customer's confirmation of their
/// own dispute (D10). An office's email names no actor — the stored "A customer" is English, and would sit inside the
/// Arabic — and opens the console in the area the recipient's role signs them into; a customer's opens the website.
/// </summary>
public sealed class OfficeNotificationEmailTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 5, 0, 0, TimeSpan.Zero); // 08:00 in Amman

    private const string Console = "https://console.khadra.test";
    private const string Website = "https://khadra.test";

    private static readonly NotificationKind[] OfficeKinds =
    [
        NotificationKind.DisputeOpened,
        NotificationKind.DisputeResolved,
        NotificationKind.BookingCompleted,
        NotificationKind.BookingMarkedNoShow,
        NotificationKind.BookingExpiredUnpaid,
        NotificationKind.BookingCancelledByAdmin,
        NotificationKind.SettlementRecorded,
        NotificationKind.SettlementVoided,
    ];

    private static NotificationMessageComposer Composer()
    {
        var calendar = Substitute.For<IReportingCalendar>();
        calendar.DayOf(Arg.Any<DateTimeOffset>()).Returns(ci => DateOnly.FromDateTime(ci.Arg<DateTimeOffset>().AddHours(3).DateTime));
        calendar.TimeOfDay(Arg.Any<DateTimeOffset>()).Returns(ci => TimeOnly.FromDateTime(ci.Arg<DateTimeOffset>().AddHours(3).DateTime));
        var payments = Substitute.For<IPaymentProvider>();
        payments.Mode.Returns(PaymentMode.Sandbox);
        return new NotificationMessageComposer(
            calendar, payments, Options.Create(new AppOptions { ClientBaseUrl = Console + "/", CustomerAppBaseUrl = Website }));
    }

    private static User Owner(Language? language = null)
    {
        var owner = User.RegisterDealerOwner(
            EmailAddress.Create("owner@petra.test").Value,
            PhoneNumber.Create("0791112223").Value,
            PersonName.Create("Rana Haddad").Value,
            PasswordHash.FromHash("hashed"),
            Now);
        if (language is not null)
            owner.ChoosePreferredLanguage(language);
        return owner;
    }

    private static User Employee() =>
        User.CreateEmployee(
            EmailAddress.Create("staff@petra.test").Value,
            PhoneNumber.Create("0791112224").Value,
            PersonName.Create("Sami Odeh").Value,
            PasswordHash.FromHash("hashed"),
            Now);

    /// <summary>As the producers raise them: the platform's, or a customer's with the stored English stand-in.</summary>
    private static Notification Raised(NotificationKind kind, Id subject, string reference = "KH-OFFICE1", string actor = Notification.PlatformActorName) =>
        Notification.Raise(Id.New(), kind, actor, Now, subject, reference);

    [Fact]
    public void Every_kind_an_office_is_emailed_has_its_own_words_in_both_languages_and_names_no_actor()
    {
        foreach (var kind in OfficeKinds)
        {
            Assert.Equal([NotificationChannel.Email], kind.DeliveredOn());
            var notification = Raised(kind, Id.New(), actor: "A customer");

            var english = Composer().ComposeEmail(notification, Owner(Language.English));
            var arabic = Composer().ComposeEmail(notification, Owner(Language.Arabic));

            Assert.NotEqual("Khadra", english.Subject);
            Assert.NotEqual("خضرا", arabic.Subject);
            Assert.Contains("KH-OFFICE1", english.TextBody, StringComparison.Ordinal);
            Assert.Contains("KH-OFFICE1", arabic.TextBody, StringComparison.Ordinal);
            Assert.DoesNotContain("A customer", english.TextBody, StringComparison.Ordinal);
            Assert.DoesNotContain("A customer", arabic.TextBody, StringComparison.Ordinal);
            Assert.DoesNotContain("Hi ", arabic.TextBody, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void An_office_email_opens_the_console_in_the_area_the_recipients_role_signs_them_into()
    {
        var booking = Id.New();
        var expired = Raised(NotificationKind.BookingExpiredUnpaid, booking);

        var owner = Composer().ComposeEmail(expired, Owner(Language.English));
        var staff = Composer().ComposeEmail(expired, Employee());

        Assert.Contains($"{Console}/dealer/bookings/{booking.Value}", owner.TextBody, StringComparison.Ordinal);
        Assert.Contains("Open the booking", owner.HtmlBody, StringComparison.Ordinal);
        Assert.Contains($"{Console}/employee/bookings/{booking.Value}", staff.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain(Website, owner.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dispute_opens_at_the_dispute_and_a_settlement_at_Payouts()
    {
        var ticket = Id.New();
        var dispute = Composer().ComposeEmail(Raised(NotificationKind.DisputeOpened, ticket, actor: "A customer"), Owner(Language.Arabic));
        var settlement = Composer().ComposeEmail(
            Raised(NotificationKind.SettlementRecorded, Id.New(), reference: "TEST-SET-2026-000001"), Employee());

        Assert.Contains($"{Console}/dealer/disputes/{ticket.Value}", dispute.TextBody, StringComparison.Ordinal);
        Assert.Contains("افتح النزاع", dispute.HtmlBody, StringComparison.Ordinal);
        Assert.Contains($"{Console}/employee/payouts", settlement.TextBody, StringComparison.Ordinal);
        Assert.Contains("TEST-SET-2026-000001", settlement.TextBody, StringComparison.Ordinal);
        // No amount travels: the row holds none.
        Assert.DoesNotContain("JOD", settlement.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void An_office_member_who_never_chose_a_language_reads_both_Arabic_first()
    {
        var email = Composer().ComposeEmail(Raised(NotificationKind.BookingCancelledByAdmin, Id.New()), Owner());

        Assert.Equal("ألغت خضرا الحجز KH-OFFICE1 · Khadra cancelled booking KH-OFFICE1", email.Subject);
    }

    /// <summary>
    /// The customer's own dispute (D10): what happens now, without claiming no money moves — an ending's refund above
    /// the deposit is still sent while a dispute is open — and the frozen moment Khadra aims to decide by, in Amman
    /// time. The email opens the booking, which links the dispute; so does the push, on every installed app.
    /// </summary>
    [Fact]
    public void The_customers_dispute_confirmation_states_the_frozen_moment_and_opens_the_booking()
    {
        var booking = Id.New();
        var due = Now.AddHours(48);
        var confirmation = Notification.Raise(
            Id.New(), NotificationKind.YourDisputeOpened, Notification.PlatformActorName, Now, booking, "KH-DISPUTE", dueAt: due);

        var push = Composer().ComposePush(confirmation, Language.English);
        var pushAr = Composer().ComposePush(confirmation, Language.Arabic);
        var email = Composer().ComposeEmail(confirmation, Users.Customer());

        Assert.Equal("Dispute opened", push.Title);
        Assert.Contains("KH-DISPUTE", push.Body, StringComparison.Ordinal);
        Assert.Contains("08:00, 2026-10-08", push.Body, StringComparison.Ordinal);
        Assert.Contains("Nothing is charged to you", push.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("money", push.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("⁨08:00, 2026-10-08⁩", pushAr.Body, StringComparison.Ordinal);
        Assert.Contains($"{Website}/bookings/{booking.Value}", email.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain(Console, email.TextBody, StringComparison.Ordinal);
    }
}
