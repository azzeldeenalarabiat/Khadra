using Khadra.Application.Bookings.Dtos;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Infrastructure.Configuration;
using Khadra.Infrastructure.Notifications;
using Khadra.Tests.Support;
using Microsoft.Extensions.Options;

namespace Khadra.Tests.Application.Bookings;

/// <summary>
/// The email a customer gets when a gallery says yes.
/// </summary>
/// <remarks>
/// <para>
/// It has two hours to do its job, and it is the only channel that reaches somebody who is not
/// holding their phone (pre-launch items 73 and 90). What it has to carry is therefore not a matter
/// of taste: the decision, the car, the money, the deadline as an ABSOLUTE moment, and a way back.
/// </para>
/// <para>
/// The absolute deadline is the one that would be easiest to get wrong and hardest to notice. "Two
/// hours" in an inbox is useless — the reader has no idea when the message was sent — and an instant
/// rendered in UTC would send a customer in Amman to pay three hours late.
/// </para>
/// </remarks>
public sealed class BookingEmailComposerTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    private static BookingEmailComposer Composer(string customerAppUrl = "") =>
        new(
            Options.Create(new AppOptions
            {
                ClientBaseUrl = "https://console.khadra.test",
                CustomerAppBaseUrl = customerAppUrl,
            }),
            TestBusinessRules.Calendar());

    /// <summary>An approved booking, priced the way a handler prices one.</summary>
    private static (BookingDto Booking, BookingContext Context) Approved(
        decimal dailyRate = 55m,
        TimeSpan? paymentWindow = null)
    {
        var start = Now.AddDays(7);
        var period = DateRange.Create(start, start.AddDays(4)).Value;
        var terms = Build.Terms(paymentWindow: paymentWindow ?? TimeSpan.FromHours(2));
        var booking = Build.Booking(
            Now,
            period: period,
            terms: terms,
            pricing: Build.Pricing(dailyRate: dailyRate, days: 4, pickupDate: Build.AmmanDate(start)));

        Assert.True(booking.Approve(Id.New(), Now).IsSuccess);

        var context = new BookingContext(
            new VehicleLabel(Guid.NewGuid(), "Kia", "Sportage", 2024, "White", "12-34567", null),
            "Rami Haddad Rentals",
            false,
            null,
            "Nour Al-Masri",
            false,
            null,
            null);

        return (BookingDto.From(booking, context, Now), context);
    }

    [Fact]
    public void It_says_who_approved_what_in_both_languages()
    {
        var (booking, context) = Approved();

        var message = Composer().BookingApproved(Build.Customer(), booking, context, note: null);

        // The gallery and the car, in both halves.
        Assert.Contains("Rami Haddad Rentals", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("Kia Sportage 2024", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("has approved your booking", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("وافق", message.HtmlBody, StringComparison.Ordinal);

        // The reference, which is how a customer and a gallery talk about one booking.
        Assert.Contains(booking.Reference, message.Subject, StringComparison.Ordinal);
        Assert.Contains(booking.Reference, message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains(booking.Reference, message.TextBody, StringComparison.Ordinal);

        // Arabic first, and each half carrying its own direction: the English below must stay
        // left-to-right or its punctuation lands at the wrong end.
        Assert.Contains("<div dir=\"rtl\" lang=\"ar\"", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("<div dir=\"ltr\" lang=\"en\"", message.HtmlBody, StringComparison.Ordinal);
        Assert.True(
            message.HtmlBody.IndexOf("dir=\"rtl\"", StringComparison.Ordinal)
                < message.HtmlBody.IndexOf("dir=\"ltr\"", StringComparison.Ordinal),
            "Arabic comes first.");
    }

    [Fact]
    public void It_states_the_deposit_at_the_platforms_own_precision()
    {
        // 55 a day for four days is 220; a fifth of that is 44.
        var (booking, context) = Approved(dailyRate: 55m);

        var message = Composer().BookingApproved(Build.Customer(), booking, context, note: null);

        // THREE decimals. A dinar has three, and a deposit printed as 44.00 is a different number
        // from the one the app and the invoice show (pre-launch item 16).
        Assert.Contains("44.000 JOD", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("44.000 JOD", message.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("44.00 JOD", message.TextBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// The deadline is an absolute Amman moment, with the zone named.
    /// </summary>
    /// <remarks>
    /// Approved at 10:00 UTC with a two-hour window, so the deposit is due at 12:00 UTC — which is
    /// 15:00 in Amman. An email that printed 12:00 would send somebody to pay three hours late.
    /// </remarks>
    [Fact]
    public void It_gives_the_deadline_as_a_moment_and_not_only_as_a_duration()
    {
        var (booking, context) = Approved();

        var message = Composer().BookingApproved(Build.Customer(), booking, context, note: null);

        Assert.Equal(Now.AddHours(2), booking.PaymentDeadline);
        Assert.Contains("2026-09-03 15:00 (Asia/Amman)", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("2026-09-03 15:00 (Asia/Amman)", message.TextBody, StringComparison.Ordinal);

        // And the duration beside it, from the booking's OWN frozen terms.
        Assert.Contains("2 hours from the approval", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("ساعتان من الآن", message.HtmlBody, StringComparison.Ordinal);
    }

    /// <summary>Arabic counts hours with words, not with a digit parked in front of a noun.</summary>
    /// <remarks>
    /// The app spells the same rule out in ICU plurals; an email has no ICU, so the composer spells
    /// it out and this checks the two agree. "2 ساعة" is what it said until the payment window became
    /// two hours, and it is the shape a reader notices immediately.
    /// </remarks>
    [Theory]
    [InlineData(1, "ساعة واحدة", "1 hour")]
    [InlineData(2, "ساعتان", "2 hours")]
    [InlineData(3, "ساعات", "3 hours")]
    [InlineData(10, "ساعات", "10 hours")]
    [InlineData(24, "ساعة", "24 hours")]
    public void The_hours_are_counted_the_way_each_language_counts(int hours, string arabic, string english)
    {
        var (booking, context) = Approved(paymentWindow: TimeSpan.FromHours(hours));

        var message = Composer().BookingApproved(Build.Customer(), booking, context, note: null);

        Assert.Contains(arabic, message.TextBody, StringComparison.Ordinal);
        Assert.Contains(english, message.TextBody, StringComparison.Ordinal);
        // Never a bare digit in front of the Arabic noun.
        Assert.DoesNotContain($"{hours} ساعة من", message.TextBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every Latin run inside the Arabic half is isolated, so the bidi algorithm cannot reorder it.
    /// </summary>
    /// <remarks>
    /// A gallery's name, a reference, a price and a date are all Latin, all inside Arabic sentences
    /// here, and without an isolate the algorithm resolves each against whatever sits beside it — a
    /// reference reads backwards, a price loses its currency code to the comma after it. The app
    /// learned this in `Formats.money`; the same characters do the same job in an inbox.
    /// </remarks>
    [Fact]
    public void Latin_inside_the_Arabic_half_is_isolated()
    {
        var (booking, context) = Approved();

        var message = Composer().BookingApproved(Build.Customer(), booking, context, note: null);
        var arabicHalf = message.HtmlBody[..message.HtmlBody.IndexOf("<hr", StringComparison.Ordinal)];

        // Whole runs, not fragments: the zone is part of the deadline's run, not a run of its own.
        foreach (var run in new[]
        {
            "Rami Haddad Rentals",
            booking.Reference,
            "44.000 JOD",
            "2026-09-03 15:00 (Asia/Amman)",
        })
        {
            var at = arabicHalf.IndexOf(run, StringComparison.Ordinal);
            Assert.True(at > 0, $"{run} appears in the Arabic half");
            Assert.Equal('⁨', arabicHalf[at - 1]);
            Assert.Equal('⁩', arabicHalf[at + run.Length]);
        }

        // The English half carries none of them: they would be noise in a left-to-right paragraph.
        var englishHalf = message.HtmlBody[message.HtmlBody.IndexOf("<hr", StringComparison.Ordinal)..];
        Assert.DoesNotContain('⁨', englishHalf);
    }

    [Fact]
    public void The_duration_follows_the_window_the_booking_froze()
    {
        var (booking, context) = Approved(paymentWindow: TimeSpan.FromHours(24));

        var message = Composer().BookingApproved(Build.Customer(), booking, context, note: null);

        // Not today's setting: what THIS booking promised. A window lengthened tomorrow must not
        // rewrite what a customer was told yesterday.
        Assert.Contains("24 hours from the approval", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("2026-09-04 13:00 (Asia/Amman)", message.HtmlBody, StringComparison.Ordinal);
    }

    /// <summary>Digits stay Latin, and no month name is spelled out in either language.</summary>
    /// <remarks>
    /// An `ar-JO` culture would render Arabic-Indic digits and Arabic month names. A deadline is
    /// compared against a clock, a phone and a bank statement, and all three use Latin figures here.
    /// </remarks>
    [Fact]
    public void The_deadline_is_written_in_the_digits_a_clock_uses()
    {
        var (booking, context) = Approved();

        var message = Composer().BookingApproved(Build.Customer(), booking, context, note: null);

        foreach (var arabicIndic in "٠١٢٣٤٥٦٧٨٩")
        {
            Assert.DoesNotContain(arabicIndic.ToString(), message.TextBody, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("September", message.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("سبتمبر", message.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void The_gallerys_own_words_are_carried_and_escaped()
    {
        var (booking, context) = Approved();

        var message = Composer().BookingApproved(
            Build.Customer(), booking, context, note: "Ask for Yousef & bring your <licence>.");

        // Escaped in the HTML half…
        Assert.Contains("Yousef &amp; bring your &lt;licence&gt;.", message.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("<licence>", message.HtmlBody, StringComparison.Ordinal);
        // …and readable in the text half, which is what a plain-text client and a spam filter see.
        Assert.Contains("Ask for Yousef & bring your <licence>.", message.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void A_note_nobody_wrote_adds_no_empty_line()
    {
        var (booking, context) = Approved();

        var message = Composer().BookingApproved(Build.Customer(), booking, context, note: "   ");

        Assert.DoesNotContain("From the office", message.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("من المكتب", message.HtmlBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// With nowhere for a customer to be sent, the email says so instead of linking to the console.
    /// </summary>
    /// <remarks>
    /// `App:ClientBaseUrl` is the dealer and admin console. A customer following a link there lands
    /// on a sign-in that refuses them, which is worse than no link: it reads as the platform being
    /// broken at the exact moment they are trying to pay. Pre-launch item 91.
    /// </remarks>
    [Fact]
    public void With_no_customer_app_url_it_names_the_app_rather_than_linking_to_the_console()
    {
        var (booking, context) = Approved();

        var message = Composer(customerAppUrl: string.Empty)
            .BookingApproved(Build.Customer(), booking, context, note: null);

        Assert.DoesNotContain("href=", message.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("console.khadra.test", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("Open the Khadra app to pay.", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("افتح تطبيق خضرا", message.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void With_one_configured_it_links_straight_to_the_booking()
    {
        var (booking, context) = Approved();

        var message = Composer(customerAppUrl: "https://app.khadra.jo/")
            .BookingApproved(Build.Customer(), booking, context, note: null);

        var expected = $"https://app.khadra.jo/bookings/{booking.BookingId}";
        Assert.Contains($"href=\"{expected}\"", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains(expected, message.TextBody, StringComparison.Ordinal);
        // One link, offered to both readers. Two would invite a second tap on a page already open.
        Assert.Equal(2, Occurrences(message.HtmlBody, expected));
        // And STILL never the console, configured or not. The two settings exist so that a customer
        // cannot be sent to a sign-in that refuses them.
        Assert.DoesNotContain("console.khadra.test", message.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("console.khadra.test", message.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void It_never_promises_that_anything_was_taken()
    {
        var (booking, context) = Approved();

        var message = Composer().BookingApproved(Build.Customer(), booking, context, note: null);

        // Nothing is charged at approval, and nothing is charged if the window runs out either.
        Assert.Contains("nothing is charged", message.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("لن يُخصم منك شيء", message.HtmlBody, StringComparison.Ordinal);
    }

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        var index = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
