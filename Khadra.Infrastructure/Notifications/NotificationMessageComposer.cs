using System.Globalization;
using System.Net;
using Khadra.Application.Common.Ports;
using Khadra.Application.Notifications.Delivery;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.Notifications;
using Khadra.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Notifications;

/// <summary>
/// The words of a push, and of a reminder email, per kind and per language.
/// </summary>
/// <remarks>
/// <para>
/// <b>Kept in step with the app by hand.</b> The in-app list says "{gallery} approved your booking";
/// the push says the same, with the booking reference so a customer with two bookings knows which.
/// A kind missing from the table below gets a neutral line, never an empty push.
/// </para>
/// <para>
/// <b>Times are Amman wall-clock, digits Latin</b> — the rule every other message here follows. A
/// time a reader compares against a clock is written in the digits every clock in Jordan uses.
/// </para>
/// <para>
/// <b>"Pay by" only when paying is possible.</b> With no payment provider every checkout is refused,
/// and a push telling a customer to pay would be a promise the platform cannot keep (the reason
/// <c>YourDepositDue</c> was never added). The approval push reads the provider's own mode and says
/// "approved — open Khadra" instead.
/// </para>
/// </remarks>
internal sealed class NotificationMessageComposer(
    IReportingCalendar calendar,
    IPaymentProvider payments,
    IOptions<AppOptions> app) : INotificationMessageComposer
{
    private sealed record Wording(string TitleEn, string BodyEn, string TitleAr, string BodyAr);

    // {actor} gallery · {ref} booking reference · {due} the frozen moment, Amman time.
    private static readonly Dictionary<string, Wording> Texts = new(StringComparer.Ordinal)
    {
        ["YourBookingApproved"] = new(
            "Booking approved", "{actor} approved booking {ref}. Open Khadra to see what's next.",
            "تمت الموافقة على حجزك", "وافق {actor} على الحجز {ref}. افتح خضرا لمعرفة الخطوة التالية."),
        ["YourBookingApproved:Pay"] = new(
            "Booking approved — deposit due", "{actor} approved booking {ref}. Pay the deposit by {due} to keep it.",
            "تمت الموافقة — العربون مستحق", "وافق {actor} على الحجز {ref}. ادفع العربون قبل {due} للاحتفاظ به."),
        ["YourBookingRejected"] = new(
            "Booking declined", "{actor} declined booking {ref}.",
            "رُفض حجزك", "رفض {actor} الحجز {ref}."),
        ["YourBookingExpired"] = new(
            "Booking ran out of time", "Your booking {ref} with {actor} ran out of time. Nothing is owed.",
            "انتهى وقت حجزك", "انتهى وقت حجزك {ref} مع {actor}. لا شيء مستحق عليك."),
        ["YourBookingConfirmed"] = new(
            "Booking confirmed", "Your deposit arrived. Booking {ref} with {actor} is confirmed.",
            "تم تأكيد حجزك", "وصل العربون. تم تأكيد حجزك {ref} مع {actor}."),
        ["YourBookingCompleted"] = new(
            "Rental finished", "Your rental {ref} with {actor} is finished. Thank you for using Khadra.",
            "انتهى الإيجار", "انتهى إيجارك {ref} مع {actor}. شكراً لاستخدامك خضرا."),
        ["YourBookingMarkedNoShow"] = new(
            "Car not collected", "{actor} recorded that booking {ref} was not collected.",
            "لم تُستلم السيارة", "سجّل {actor} أن الحجز {ref} لم يُستلم."),
        ["YourBookingCancelled"] = new(
            "Booking cancelled", "Booking {ref} with {actor} was cancelled. Open Khadra for details.",
            "أُلغي حجزك", "أُلغي الحجز {ref} مع {actor}. افتح خضرا للتفاصيل."),
        ["YourBookingPickedUp"] = new(
            "Car collected", "You collected the car for booking {ref} from {actor}. Have a good trip.",
            "تم استلام السيارة", "استلمت سيارة الحجز {ref} من {actor}. رحلة موفقة."),
        ["YourBookingReturned"] = new(
            "Car returned", "{actor} recorded the return of booking {ref}.",
            "تمت إعادة السيارة", "سجّل {actor} إعادة سيارة الحجز {ref}."),
        ["YourPaymentReminder"] = new(
            "Deposit still due", "Pay the deposit for booking {ref} by {due}, or it will expire.",
            "العربون ما زال مستحقاً", "ادفع عربون الحجز {ref} قبل {due}، وإلا انتهى الحجز."),
        ["YourPickupReminder"] = new(
            "Pickup soon", "Your car for booking {ref} is ready at {actor} at {due}. Show your handover code at the counter.",
            "موعد الاستلام قريب", "سيارة الحجز {ref} جاهزة لدى {actor} الساعة {due}. أظهر رمز التسليم عند المكتب."),
        ["YourReturnReminder"] = new(
            "Return soon", "Booking {ref} is due back at {actor} at {due}.",
            "موعد الإعادة قريب", "موعد إعادة سيارة الحجز {ref} إلى {actor} الساعة {due}."),
        ["YourDisputeUpdated"] = new(
            "Dispute updated", "There is an update on the dispute for booking {ref}.",
            "تحديث على النزاع", "هناك تحديث على النزاع الخاص بالحجز {ref}."),
    };

    private static readonly Wording Fallback = new(
        "Khadra", "There is an update on booking {ref}.",
        "خضرا", "هناك تحديث على الحجز {ref}.");

    public PushText ComposePush(Notification notification, Language language)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ArgumentNullException.ThrowIfNull(language);

        var wording = WordingFor(notification);
        var arabic = language == Language.Arabic;
        return new PushText(
            Fill(arabic ? wording.TitleAr : wording.TitleEn, notification, isolate: false),
            Fill(arabic ? wording.BodyAr : wording.BodyEn, notification, isolate: arabic));
    }

    public EmailMessage ComposeEmail(Notification notification, User recipient)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ArgumentNullException.ThrowIfNull(recipient);

        var wording = WordingFor(notification);
        var name = recipient.Name.Value;
        var baseUrl = app.Value.CustomerAppBaseUrl.TrimEnd('/');
        var link = baseUrl.Length > 0 && notification.SubjectId is { } subject
            ? $"{baseUrl}/bookings/{subject.Value}"
            : null;

        var arabicBody = Fill(wording.BodyAr, notification, isolate: true);
        var englishBody = Fill(wording.BodyEn, notification, isolate: false);
        var arabicTitle = Fill(wording.TitleAr, notification, isolate: false);
        var englishTitle = Fill(wording.TitleEn, notification, isolate: false);

        var arabicHtml = $"""<div dir="rtl" lang="ar" style="text-align:right"><p>مرحباً {Html(name)}،</p><p>{Html(arabicBody)}</p>"""
                         + (link is null ? string.Empty : $"""<p><a href="{link}">افتح الحجز</a></p>""") + "</div>";
        var englishHtml = $"""<div dir="ltr" lang="en"><p>Hi {Html(name)},</p><p>{Html(englishBody)}</p>"""
                          + (link is null ? string.Empty : $"""<p><a href="{link}">Open the booking</a></p>""") + "</div>";
        var arabicText = $"مرحباً {name}،\n\n{arabicBody}\n" + (link is null ? string.Empty : $"{link}\n");
        var englishText = $"Hi {name},\n\n{englishBody}\n" + (link is null ? string.Empty : $"{link}\n");

        // One language when the person chose one; both, Arabic first, when they never have — which is
        // how every other email on this platform reads until they do.
        return recipient.PreferredLanguage switch
        {
            var chosen when chosen == Language.Arabic => new EmailMessage(
                recipient.Email.Value, name, arabicTitle, arabicHtml, arabicText),
            var chosen when chosen == Language.English => new EmailMessage(
                recipient.Email.Value, name, englishTitle, englishHtml, englishText),
            _ => new EmailMessage(
                recipient.Email.Value,
                name,
                $"{arabicTitle} · {englishTitle}",
                arabicHtml + """<hr style="border:none;border-top:1px solid #ddd;margin:20px 0">""" + englishHtml,
                arabicText + "\n----------\n\n" + englishText),
        };
    }

    private Wording WordingFor(Notification notification)
    {
        var key = notification.Kind.Name;
        if (key == "YourBookingApproved" && notification.DueAt is not null && payments.Mode != PaymentMode.None)
            key = "YourBookingApproved:Pay";
        return Texts.GetValueOrDefault(key, Fallback);
    }

    private string Fill(string template, Notification notification, bool isolate)
    {
        string Run(string value) => isolate ? $"⁨{value}⁩" : value;

        return template
            .Replace("{actor}", Run(notification.ActorName), StringComparison.Ordinal)
            .Replace("{ref}", Run(notification.SubjectReference ?? string.Empty), StringComparison.Ordinal)
            .Replace("{due}", notification.DueAt is { } due ? Run(Moment(due)) : string.Empty, StringComparison.Ordinal);
    }

    private string Moment(DateTimeOffset instant)
    {
        var day = calendar.DayOf(instant);
        var time = calendar.TimeOfDay(instant);
        return string.Create(CultureInfo.InvariantCulture, $"{time:HH\\:mm}, {day:yyyy-MM-dd}");
    }

    private static string Html(string value) => WebUtility.HtmlEncode(value);
}
