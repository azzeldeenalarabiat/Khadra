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
/// The words of a push and of an email, per kind and per language: the customer's, and since Fix & Polish Wave 3 (C5)
/// the office's.
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
/// <para>
/// <b>An office's email names no actor.</b> Its kinds are what the platform or a customer did, and a customer is
/// never named to an office: the stored "A customer" is English, and would sit inside the Arabic. Its link opens the
/// console, `/dealer` for the owner and `/employee` for staff; a customer's only ever opens the website.
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
            // One kind for both ways to pay (owner, 2026-09-25): "deposit" was wrong for a booking
            // paid in full, and the notification row does not record which it was.
            "Booking confirmed", "Your payment arrived. Booking {ref} with {actor} is confirmed.",
            "تم تأكيد حجزك", "وصلت دفعتك. تم تأكيد حجزك {ref} مع {actor}."),
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
        ["YourDepositRefunded"] = new(
            // Fires for a deposit AND for a booking paid in full, so it says "payment".
            "Payment refunded", "Booking {ref} with {actor}: your payment has been refunded to your original payment method. Your bank may take some time to show it.",
            "تم استرداد دفعتك", "الحجز {ref} مع {actor}: تم استرداد دفعتك إلى وسيلة الدفع الأصلية. قد يحتاج البنك بعض الوقت لإظهار المبلغ في حسابك."),
        ["YourPartialRefundSettled"] = new(
            // Part of the payment is back and part is not (yet): never "your payment has been refunded".
            "Part of your payment refunded", "Booking {ref} with {actor}: part of your payment has been refunded to your original payment method. Open Khadra to see the amount. Your bank may take some time to show it.",
            "تم استرداد جزء من دفعتك", "الحجز {ref} مع {actor}: تم استرداد جزء من دفعتك إلى وسيلة الدفع الأصلية. افتح خضرا لمعرفة المبلغ. قد يحتاج البنك بعض الوقت لإظهاره في حسابك."),
        // The platform's own refunds (Wave 2 C6; E2E F48): an administrator's dispute decision or cancellation. They
        // name no rental office, which did not make them.
        ["YourDepositRefunded:Platform"] = new(
            "Payment refunded", "Khadra has refunded your payment for booking {ref} to your original payment method. Your bank may take some time to show it.",
            "تم استرداد دفعتك", "أعادت خضرا دفعتك للحجز {ref} إلى وسيلة الدفع الأصلية. قد يحتاج البنك بعض الوقت لإظهار المبلغ في حسابك."),
        ["YourPartialRefundSettled:Platform"] = new(
            "Part of your payment refunded", "Khadra has refunded part of your payment for booking {ref} to your original payment method. Open Khadra to see the amount. Your bank may take some time to show it.",
            "تم استرداد جزء من دفعتك", "أعادت خضرا جزءًا من دفعتك للحجز {ref} إلى وسيلة الدفع الأصلية. افتح خضرا لمعرفة المبلغ. قد يحتاج البنك بعض الوقت لإظهاره في حسابك."),
        ["YourDisputeUpdated"] = new(
            "Dispute updated", "There is an update on the dispute for booking {ref}.",
            "تحديث على النزاع", "هناك تحديث على النزاع الخاص بالحجز {ref}."),
        // The customer's own dispute, confirmed (Wave 3, D10). "No money moves" would be false: an ending's refund
        // above the deposit is still sent while a dispute is open. {due} is the ticket's frozen SLA deadline.
        ["YourDisputeOpened"] = new(
            "Dispute opened", "Your dispute on booking {ref} is open. Nothing is charged to you, and nothing held on this booking is released, until Khadra decides. Khadra aims to decide by {due}.",
            "فُتح نزاعك", "نزاعك على الحجز {ref} مفتوح. لن يُحمَّل عليك شيء، ولن يُفرَج عن شيء محجوز على هذا الحجز، حتى تقرّر خضرا. وتسعى خضرا إلى القرار قبل {due}."),
        // Khadra could not accept one of the customer's documents (Wave 4, W4-9). No {ref} and never the reason: the
        // notice has no subject, and a lock screen is not a private channel. The documents page says which and why.
        ["YourDocumentRejected"] = new(
            "A document needs a new upload", "Khadra could not accept one of your documents. Open your documents to see which and why, and upload a new one.",
            "مستند يحتاج إلى رفع جديد", "لم تتمكن خضرا من قبول أحد مستنداتك. افتح مستنداتك لمعرفة أيّها والسبب، وارفع نسخة جديدة."),

        // The office's (Wave 3, C5), by email only. No {actor}: see the remarks above.
        ["DisputeOpened"] = new(
            "Dispute opened on {ref}", "A dispute was opened on booking {ref}. Khadra will review it; open it to read it and to add your office's statement.",
            "فُتح نزاع على الحجز {ref}", "فُتح نزاع على الحجز {ref}. ستراجعه خضرا؛ افتحه لقراءته ولإضافة إفادة مكتبك."),
        ["DisputeResolved"] = new(
            "Dispute decided on {ref}", "Khadra decided the dispute on booking {ref}. Open it to see the decision and what it records for your office.",
            "حُسم النزاع على الحجز {ref}", "حسمت خضرا النزاع على الحجز {ref}. افتحه لمعرفة القرار وما يسجّله لمكتبك."),
        ["BookingCompleted"] = new(
            "Booking {ref} completed", "Booking {ref} is complete: the car is back, and nothing on it is in dispute.",
            "اكتمل الحجز {ref}", "اكتمل الحجز {ref}: عادت السيارة، وليس عليه نزاع قائم."),
        ["BookingMarkedNoShow"] = new(
            "Booking {ref} marked a no-show", "Booking {ref} was marked a no-show: the customer did not collect the car in time.",
            "سُجّل عدم حضور على الحجز {ref}", "سُجّل عدم حضور على الحجز {ref}: لم يستلم العميل السيارة في الوقت المحدد."),
        ["BookingExpiredUnpaid"] = new(
            "Booking {ref} expired unpaid", "Booking {ref} expired: the customer did not pay by the payment deadline. The car is free for those dates again.",
            "انتهى الحجز {ref} دون دفع", "انتهى الحجز {ref}: لم يدفع العميل قبل انتهاء مهلة الدفع. أصبحت السيارة متاحة لتلك التواريخ من جديد."),
        ["BookingCancelledByAdmin"] = new(
            "Khadra cancelled booking {ref}", "Khadra cancelled booking {ref}. Open it to see the reason, which the customer is shown too.",
            "ألغت خضرا الحجز {ref}", "ألغت خضرا الحجز {ref}. افتحه لمعرفة السبب، وهو ما يُعرض على العميل أيضاً."),
        // A settlement's number, never its amount: the notification holds none.
        ["SettlementRecorded"] = new(
            "Settlement {ref} recorded", "Khadra recorded settlement {ref} with your office. Open Payouts to see what it covers.",
            "سُجّلت التسوية {ref}", "سجّلت خضرا التسوية {ref} مع مكتبك. افتح صفحة التحويلات لمعرفة ما تشمله."),
        ["SettlementVoided"] = new(
            "Settlement {ref} voided", "Khadra voided settlement {ref}. What it covered is due again; open Payouts to see it.",
            "أُلغيت التسوية {ref}", "ألغت خضرا التسوية {ref}. ما كانت تشمله مستحق من جديد؛ افتح صفحة التحويلات لمعرفته."),
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
            Fill(arabic ? wording.TitleAr : wording.TitleEn, notification, arabic, isolate: false),
            Fill(arabic ? wording.BodyAr : wording.BodyEn, notification, arabic, isolate: arabic));
    }

    public EmailMessage ComposeEmail(Notification notification, User recipient)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ArgumentNullException.ThrowIfNull(recipient);

        var wording = WordingFor(notification);
        var name = recipient.Name.Value;
        var link = LinkFor(notification, recipient);

        var arabicBody = Fill(wording.BodyAr, notification, arabic: true, isolate: true);
        var englishBody = Fill(wording.BodyEn, notification, arabic: false, isolate: false);
        var arabicTitle = Fill(wording.TitleAr, notification, arabic: true, isolate: false);
        var englishTitle = Fill(wording.TitleEn, notification, arabic: false, isolate: false);

        var arabicHtml = $"""<div dir="rtl" lang="ar" style="text-align:right"><p>مرحباً {Html(name)}،</p><p>{Html(arabicBody)}</p>"""
                         + (link is null ? string.Empty : $"""<p><a href="{link.Url}">{link.Arabic}</a></p>""") + "</div>";
        var englishHtml = $"""<div dir="ltr" lang="en"><p>Hi {Html(name)},</p><p>{Html(englishBody)}</p>"""
                          + (link is null ? string.Empty : $"""<p><a href="{link.Url}">{link.English}</a></p>""") + "</div>";
        var arabicText = $"مرحباً {name}،\n\n{arabicBody}\n" + (link is null ? string.Empty : $"{link.Url}\n");
        var englishText = $"Hi {name},\n\n{englishBody}\n" + (link is null ? string.Empty : $"{link.Url}\n");

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

    private sealed record Link(string Url, string English, string Arabic);

    /// <summary>
    /// Where an email leads (Wave 3, C5). A member of an office opens the console, in the area their role signs them
    /// into: a dispute at the dispute, a settlement at Payouts, anything else at its booking. A customer opens the
    /// website's booking page, as every customer email always has.
    /// </summary>
    private Link? LinkFor(Notification notification, User recipient)
    {
        if (recipient.Role == UserRole.DealerOwner || recipient.Role == UserRole.DealerEmployee)
        {
            var console = app.Value.ClientBaseUrl.TrimEnd('/');
            if (console.Length == 0)
                return null;

            var area = recipient.Role == UserRole.DealerOwner ? "dealer" : "employee";
            var kind = notification.Kind;
            if (kind == NotificationKind.SettlementRecorded || kind == NotificationKind.SettlementVoided)
                return new Link($"{console}/{area}/payouts", "Open Payouts", "افتح التحويلات");
            if (notification.SubjectId is not { } subject)
                return null;
            return kind == NotificationKind.DisputeOpened || kind == NotificationKind.DisputeResolved
                ? new Link($"{console}/{area}/disputes/{subject.Value}", "Open the dispute", "افتح النزاع")
                : new Link($"{console}/{area}/bookings/{subject.Value}", "Open the booking", "افتح الحجز");
        }

        var site = app.Value.CustomerAppBaseUrl.TrimEnd('/');
        if (site.Length == 0)
            return null;

        // About the account, not a booking (Wave 4, W4-9): it leads to the documents page. Language-less, as the
        // booking links are; the website sends any such path on to the reader's language.
        if (notification.Kind == NotificationKind.YourDocumentRejected)
            return new Link($"{site}/profile/documents", "Open your documents", "افتح مستنداتك");

        return notification.SubjectId is { } booking
            ? new Link($"{site}/bookings/{booking.Value}", "Open the booking", "افتح الحجز")
            : null;
    }

    private Wording WordingFor(Notification notification)
    {
        var key = notification.Kind.Name;
        if (key == "YourBookingApproved" && notification.DueAt is not null && payments.Mode != PaymentMode.None)
            key = "YourBookingApproved:Pay";
        // What the platform did, worded as the platform's where a kind has such a variant (Wave 2 C6).
        if (notification.IsFromPlatform && Texts.ContainsKey(key + ":Platform"))
            key += ":Platform";
        return Texts.GetValueOrDefault(key, Fallback);
    }

    private string Fill(string template, Notification notification, bool arabic, bool isolate)
    {
        string Run(string value) => isolate ? $"⁨{value}⁩" : value;

        return template
            .Replace("{actor}", Run(Actor(notification, arabic)), StringComparison.Ordinal)
            .Replace("{ref}", Run(notification.SubjectReference ?? string.Empty), StringComparison.Ordinal)
            .Replace("{due}", notification.DueAt is { } due ? Run(Moment(due)) : string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// The actor in the message's language. A stand-in is worded from its code (pre-launch item 103) — the stored phrase is
    /// English, and an Arabic push for a booking at an office that has since left the platform used to carry it; a
    /// named actor is the name it was recorded under.
    /// </summary>
    private static string Actor(Notification notification, bool arabic)
    {
        if (notification.ActorStandIn == NotificationStandIn.RentalOffice)
            return arabic ? "مكتب التأجير" : "The rental office";
        if (notification.ActorStandIn == NotificationStandIn.Customer)
            return arabic ? "أحد العملاء" : "A customer";
        if (notification.ActorStandIn == NotificationStandIn.Colleague)
            return arabic ? "أحد الزملاء" : "A colleague";
        return notification.ActorName;
    }

    private string Moment(DateTimeOffset instant)
    {
        var day = calendar.DayOf(instant);
        var time = calendar.TimeOfDay(instant);
        return string.Create(CultureInfo.InvariantCulture, $"{time:HH\\:mm}, {day:yyyy-MM-dd}");
    }

    private static string Html(string value) => WebUtility.HtmlEncode(value);
}
