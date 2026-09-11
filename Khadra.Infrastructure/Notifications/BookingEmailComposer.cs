using System.Globalization;
using System.Net;
using Khadra.Application.Bookings.Dtos;
using Khadra.Application.Common.Dtos;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Notifications;

/// <summary>
/// The approval email: a gallery said yes, and the deposit is owed by a deadline.
/// </summary>
/// <remarks>
/// <para>
/// <b>Both languages in one message, Arabic first</b>, for the same reason every other email on this
/// platform is: nobody stores a language on an account, and this is the one message a customer has
/// two hours to act on. Guessing wrong here does not annoy somebody — it loses them a car.
/// </para>
/// <para>
/// <b>The deadline is an absolute Amman date and time, not "two hours".</b> A relative figure is
/// useless in an inbox: it is read at an unknown remove from the moment it was sent, and the reader
/// cannot subtract. The zone is NAMED rather than assumed, from the platform's own calendar, so a
/// traveller reading this in London knows which clock it is against.
/// </para>
/// <para>
/// Digits stay LATIN and the pattern is fixed and numeric, under
/// <see cref="CultureInfo.InvariantCulture"/>. An <c>ar-JO</c> culture would render Arabic-Indic
/// digits and Arabic month names, and this platform's decision — the same one the console made — is
/// that a figure a reader has to compare against a clock is written in the digits every clock,
/// receipt and bank statement in Jordan uses.
/// </para>
/// </remarks>
internal sealed class BookingEmailComposer(
    IOptions<AppOptions> options,
    IReportingCalendar calendar) : IBookingEmailComposer
{
    private readonly string _customerAppUrl = options.Value.CustomerAppBaseUrl.TrimEnd('/');

    public EmailMessage BookingApproved(
        User customer,
        BookingDto booking,
        BookingContext context,
        string? note)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(booking);
        ArgumentNullException.ThrowIfNull(context);

        var gallery = context.DealerName;
        var car = Car(context);
        var deposit = Money(booking.Pricing.DepositAmount);
        var deadline = booking.PaymentDeadline is { } due ? Moment(due) : null;
        var reference = booking.Reference;
        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        // The booking's own frozen window, in whole hours where it is one. The email states the rule
        // this booking was made under, never the setting in force the day it is read.
        var hours = booking.Terms.PaymentWindowHours;
        var hoursText = hours == Math.Floor(hours)
            ? ((int)hours).ToString(CultureInfo.InvariantCulture)
            : hours.ToString("0.#", CultureInfo.InvariantCulture);

        var link = _customerAppUrl.Length == 0
            ? null
            : $"{_customerAppUrl}/bookings/{booking.BookingId}";

        var arabic = new List<string>
        {
            $"وافق {Html(gallery)} على حجزك لسيارة {Html(car)}.",
            $"رقم الحجز: {Html(reference)}",
            $"العربون المستحق: {Html(deposit)}",
        };
        var english = new List<string>
        {
            $"{Html(gallery)} has approved your booking for the {Html(car)}.",
            $"Booking reference: {Html(reference)}",
            $"Deposit due: {Html(deposit)}",
        };

        if (deadline is not null)
        {
            arabic.Add($"ادفع قبل {Html(deadline)} ({hoursText} ساعة من الآن)، وإلا انتهى الحجز وعادت السيارة إلى السوق.");
            english.Add($"Pay by {Html(deadline)} — {hoursText} hours from the approval — or the booking ends and the car goes back on the market.");
        }

        if (trimmedNote is not null)
        {
            arabic.Add($"من المكتب: {Html(trimmedNote)}");
            english.Add($"From the office: {Html(trimmedNote)}");
        }

        arabic.Add(link is null
            ? "افتح تطبيق خضرا لإتمام الدفع."
            : "أتمم الدفع من هنا:");
        english.Add(link is null
            ? "Open the Khadra app to pay."
            : "Finish paying here:");

        return Compose(
            customer,
            subject: $"تمت الموافقة على حجزك {reference} · Your Khadra booking {reference} was approved",
            arabicLines: arabic,
            englishLines: english,
            link: link,
            arabicAction: "ادفع العربون",
            englishAction: "Pay the deposit",
            arabicFooter: "إن لم تدفع في الوقت المحدد فلن يُخصم منك شيء؛ ينتهي الحجز فحسب.",
            englishFooter: "If you do not pay in time nothing is charged; the booking simply ends.",
            // The plain-text half has to carry the same facts unescaped.
            arabicPlain: Plain(arabic),
            englishPlain: Plain(english));
    }

    /// <summary>What the customer would recognise on the street: the car, not its database row.</summary>
    private static string Car(BookingContext context) =>
        context.Vehicle is { } vehicle
            ? $"{vehicle.Make} {vehicle.Model} {vehicle.Year.ToString(CultureInfo.InvariantCulture)}"
            // A booking outlives its listing, so the label can legitimately be gone. Saying nothing
            // about the car is better than inventing one; the reference identifies it.
            : "your booking";

    /// <summary>
    /// An amount at the platform's own precision, with its currency code.
    /// </summary>
    /// <remarks>
    /// Three decimals, because a dinar has three: 44.000 and not 44.00. Pre-launch item 16 is the
    /// record of what happens when somebody prints two.
    /// </remarks>
    private static string Money(MoneyDto money) =>
        $"{money.Amount.ToString($"N{Domain.Common.Money.MinorUnits}", CultureInfo.InvariantCulture)} {money.Currency}";

    /// <summary>An instant as a wall clock in the platform's zone, named.</summary>
    private string Moment(DateTimeOffset instant)
    {
        var day = calendar.DayOf(instant);
        var time = calendar.TimeOfDay(instant);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{day:yyyy-MM-dd} {time:HH\\:mm} ({calendar.TimeZoneId})");
    }

    private static string Html(string value) => WebUtility.HtmlEncode(value);

    private static IReadOnlyList<string> Plain(IEnumerable<string> htmlLines) =>
        [.. htmlLines.Select(line => WebUtility.HtmlDecode(line) ?? line)];

    /// <summary>
    /// One message carrying the same thing twice: Arabic, the action, then English.
    /// </summary>
    /// <remarks>
    /// A sibling of <c>AuthEmailComposer.Compose</c> rather than a share of it. That one is built
    /// around a single sentence and a single link, which is right for a verification message and
    /// wrong for one that has to lay out a car, a reference, a sum and a deadline as separate lines a
    /// reader can scan.
    ///
    /// The plain-text alternative is not an afterthought: a client that refuses HTML, and every spam
    /// filter that reads the text part to decide whether a message is worth delivering, sees only
    /// this — and an approval email that does not arrive is a booking that expires unread.
    /// </remarks>
    private static EmailMessage Compose(
        User customer,
        string subject,
        IReadOnlyList<string> arabicLines,
        IReadOnlyList<string> englishLines,
        string? link,
        string arabicAction,
        string englishAction,
        string arabicFooter,
        string englishFooter,
        IReadOnlyList<string> arabicPlain,
        IReadOnlyList<string> englishPlain)
    {
        var name = WebUtility.HtmlEncode(customer.Name.Value);

        var html =
            $"""<div dir="rtl" lang="ar" style="text-align:right"><p>مرحباً {name}،</p>"""
            + string.Concat(arabicLines.Select(line => $"<p>{line}</p>"))
            + (link is null ? string.Empty : $"""<p><a href="{link}">{arabicAction}</a></p>""")
            + $"<p>{arabicFooter}</p></div>"
            + """<hr style="border:none;border-top:1px solid #ddd;margin:20px 0">"""
            + $"""<div dir="ltr" lang="en"><p>Hi {name},</p>"""
            + string.Concat(englishLines.Select(line => $"<p>{line}</p>"))
            + (link is null ? string.Empty : $"""<p><a href="{link}">{englishAction}</a></p>""")
            + $"<p>{englishFooter}</p></div>";

        var text =
            $"مرحباً {customer.Name.Value}،\n\n"
            + string.Join("\n", arabicPlain) + "\n"
            + (link is null ? string.Empty : $"{link}\n")
            + $"\n{arabicFooter}\n\n"
            + "----------\n\n"
            + $"Hi {customer.Name.Value},\n\n"
            + string.Join("\n", englishPlain) + "\n"
            + (link is null ? string.Empty : $"{link}\n")
            + $"\n{englishFooter}";

        return new EmailMessage(customer.Email.Value, customer.Name.Value, subject, html, text);
    }
}
