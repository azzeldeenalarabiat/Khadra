using System.Text.Json.Nodes;
using Khadra.Application.FinancialDocuments.Email;
using Khadra.Domain.Common;
using Khadra.Domain.FinancialDocuments;
using Khadra.Domain.Payments;

namespace Khadra.Tests.Application.FinancialDocuments;

/// <summary>
/// A receipt's email in words (payments Phase 7), against the SHARED contract fixture: the record's own title, number,
/// headline and amount as its PDF prints them, its booking reference and frozen Amman issue time, the correction it
/// is — and the email's few sentences around them, in English, in Arabic, or both for a customer who never chose.
/// </summary>
public sealed class FinancialDocumentEmailLayoutTests
{
    private const string Lri = "⁦";
    private const string Fsi = "⁨";
    private const string Pdi = "⁩";

    // Owner, 2026-09-29: "your Khadra account", which the website and the app both open; one PDF or two, said as it is.
    private const string EnglishClosing =
        "Your receipt is attached as a PDF. You can find it, and every receipt before it, in Invoices & Receipts in your Khadra account.";

    private const string ArabicClosing =
        "إيصالك مرفق بصيغة PDF. تجده، مع كل إيصال قبله، في «الفواتير والإيصالات» في حسابك على خضرا.";

    private const string EnglishClosingBoth =
        "Your receipt is attached as two PDFs, in Arabic and English. You can find it, and every receipt before it, in Invoices & Receipts in your Khadra account.";

    private const string ArabicClosingBoth =
        "إيصالك مرفق في ملفَّي PDF، بالعربية والإنجليزية. تجده، مع كل إيصال قبله، في «الفواتير والإيصالات» في حسابك على خضرا.";

    [Fact]
    public void In_english_it_states_the_receipts_facts_as_its_pdf_prints_them_and_says_the_pdf_is_attached()
    {
        var receipt = FromFixture("payment-receipt-paid-in-full");

        var email = FinancialDocumentEmailLayout.TryCompose(receipt, "Rana Sharif", [Language.English], null)!;

        Assert.Equal("Payment receipt TEST-PAY-2026-000002", email.Subject);
        Assert.Equal(
            "Hi Rana Sharif,\n\n"
            + "Payment receipt TEST-PAY-2026-000002\n"
            + "Booking KH-FIXTURE2\n"
            + "Amount paid: JOD 94.500\n"
            + "Issued 3 Sept 2026, 15:01\n\n"
            + EnglishClosing + "\n",
            email.TextBody);
        Assert.StartsWith("""<div dir="ltr" lang="en"><p>Hi Rana Sharif,</p>""", email.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("Amount paid: JOD 94.500<br>Issued 3 Sept 2026, 15:01", email.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("<a ", email.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void In_arabic_it_reads_right_to_left_with_every_latin_run_isolated()
    {
        var receipt = FromFixture("payment-receipt-paid-in-full");

        var email = FinancialDocumentEmailLayout.TryCompose(receipt, "Rana Sharif", [Language.Arabic], null)!;

        Assert.Equal("إيصال دفع TEST-PAY-2026-000002", email.Subject);
        Assert.Equal(
            "مرحباً Rana Sharif،\n\n"
            + $"إيصال دفع {Lri}TEST-PAY-2026-000002{Pdi}\n"
            + $"الحجز {Lri}KH-FIXTURE2{Pdi}\n"
            + $"المبلغ المدفوع: {Lri}94.500 JOD{Pdi}\n"
            + $"صدر في {Fsi}3 أيلول 2026، 15:01{Pdi}\n\n"
            + ArabicClosing + "\n",
            email.TextBody);
        Assert.StartsWith("""<div dir="rtl" lang="ar" style="text-align:right">""", email.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void For_a_customer_who_never_chose_it_is_arabic_then_english_under_one_subject()
    {
        var receipt = FromFixture("refund-receipt-free-cancellation");

        var email = FinancialDocumentEmailLayout.TryCompose(receipt, "Rana Sharif", [Language.Arabic, Language.English], null)!;

        Assert.Equal("إيصال استرداد · Refund receipt TEST-RFD-2026-000001", email.Subject);
        var (arabic, english) = (email.TextBody.IndexOf("مرحباً", StringComparison.Ordinal), email.TextBody.IndexOf("Hi Rana", StringComparison.Ordinal));
        Assert.True(arabic == 0 && english > arabic, "Arabic first, then English.");
        Assert.Contains("\n----------\n\n", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("Amount refunded: JOD 94.500", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("<hr ", email.HtmlBody, StringComparison.Ordinal);

        // Two PDFs go with it, and each language says so; never the one-PDF sentence.
        Assert.Contains(ArabicClosingBoth + "\n", email.TextBody, StringComparison.Ordinal);
        Assert.Contains(EnglishClosingBoth + "\n", email.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain(EnglishClosing, email.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain(ArabicClosing, email.TextBody, StringComparison.Ordinal);
        Assert.Contains("two PDFs, in Arabic and English", email.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void A_correction_names_the_number_it_corrects_and_an_original_names_none()
    {
        var correction = FromFixture("payment-receipt-deposit-correction");

        var english = FinancialDocumentEmailLayout.TryCompose(correction, "Rana Sharif", [Language.English], null)!;
        var arabic = FinancialDocumentEmailLayout.TryCompose(correction, "Rana Sharif", [Language.Arabic], null)!;

        Assert.Contains("\n\nThis receipt corrects TEST-PAY-2026-000001, which was voided.\n\n", english.TextBody, StringComparison.Ordinal);
        Assert.Contains($"يصحّح هذا الإيصال {Lri}TEST-PAY-2026-000001{Pdi} الذي أُلغي.", arabic.TextBody, StringComparison.Ordinal);
        Assert.Contains("Issued 4 Sept 2026, 13:00", english.TextBody, StringComparison.Ordinal);

        var original = FinancialDocumentEmailLayout.TryCompose(FromFixture("payment-receipt-deposit-voided"), "Rana Sharif", [Language.English], null)!;
        Assert.DoesNotContain("corrects", original.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Once_there_is_a_customer_host_each_language_links_the_websites_page_for_the_document_in_that_language()
    {
        var receipt = FromFixture("payment-receipt-paid-in-full");
        var id = receipt.Id.Value.ToString("D");

        var email = FinancialDocumentEmailLayout.TryCompose(receipt, "Rana Sharif", [Language.Arabic, Language.English], "https://khadra.jo/")!;

        Assert.Contains($"افتحه في خضرا: https://khadra.jo/ar/invoices/{id}\n", email.TextBody, StringComparison.Ordinal);
        Assert.Contains($"Open it in Khadra: https://khadra.jo/en/invoices/{id}\n", email.TextBody, StringComparison.Ordinal);
        Assert.Contains($"""<a href="https://khadra.jo/en/invoices/{id}">Open it in Khadra</a>""", email.HtmlBody, StringComparison.Ordinal);
        // Never an address the app keeps to itself, and nothing at all while there is no customer host.
        Assert.DoesNotContain("/profile/", email.TextBody, StringComparison.Ordinal);
        Assert.Null(FinancialDocumentEmailLayout.Link("  ", Language.English, receipt.Id));
    }

    [Fact]
    public void A_name_is_escaped_in_the_html_and_kept_as_it_is_in_the_text()
    {
        var receipt = FromFixture("payment-receipt-paid-in-full");

        var email = FinancialDocumentEmailLayout.TryCompose(receipt, "<b>Rana</b> & Co", [Language.English], null)!;

        Assert.Contains("Hi &lt;b&gt;Rana&lt;/b&gt; &amp; Co,", email.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>Rana</b>", email.HtmlBody, StringComparison.Ordinal);
        Assert.StartsWith("Hi <b>Rana</b> & Co,", email.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_its_pdf_could_not_be_drawn_from_is_not_emailed_either()
    {
        var unknown = FromFixture("payment-receipt-paid-in-full", schemaVersion: 2);

        Assert.Null(FinancialDocumentEmailLayout.TryCompose(unknown, "Rana Sharif", [Language.English], null));
    }

    [Fact]
    public void The_idempotency_key_is_the_same_for_the_same_message_and_new_for_any_other()
    {
        var delivery = Id.New();
        var text = new FinancialDocumentEmailText("Payment receipt PAY-2026-000001", "<p>Hi</p>", "Hi");
        string[] pdfs = [new string('a', 64), new string('b', 64)];

        const string support = "support@khadra.jo";
        var key = EmailFinancialDocumentHandler.IdempotencyKey(delivery, "rana@example.jo", support, text, pdfs);

        Assert.Matches("^fd-[0-9a-f]{32}-[0-9a-f]{16}$", key);
        Assert.Equal(key, EmailFinancialDocumentHandler.IdempotencyKey(delivery, "rana@example.jo", support, text with { }, [.. pdfs]));
        Assert.NotEqual(key, EmailFinancialDocumentHandler.IdempotencyKey(delivery, "rana@example.jo", support, text with { TextBody = "Hello" }, pdfs));
        Assert.NotEqual(key, EmailFinancialDocumentHandler.IdempotencyKey(delivery, "rana@example.jo", support, text, [pdfs[0]]));
        Assert.NotEqual(key, EmailFinancialDocumentHandler.IdempotencyKey(Id.New(), "rana@example.jo", support, text, pdfs));
        // Sent to another address, or with replies going elsewhere, it is another message: Resend would refuse the old
        // key for it (409).
        Assert.NotEqual(key, EmailFinancialDocumentHandler.IdempotencyKey(delivery, "rana.sharif@example.jo", support, text, pdfs));
        Assert.NotEqual(key, EmailFinancialDocumentHandler.IdempotencyKey(delivery, "rana@example.jo", "help@khadra.jo", text, pdfs));
        Assert.NotEqual(key, EmailFinancialDocumentHandler.IdempotencyKey(delivery, "rana@example.jo", null, text, pdfs));
        // The key itself names no one: it travels in a header.
        Assert.DoesNotContain("rana", key, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failure_is_recorded_without_any_address_and_by_its_kind_when_it_is_not_a_transports_own_refusal()
    {
        Assert.Equal(
            "Resend refused the message (403): (an address) is not verified",
            EmailFinancialDocumentHandler.Describe(new InvalidOperationException("Resend refused the message (403): owner@gallery.jo is not verified")));
        Assert.Equal("TimeoutException", EmailFinancialDocumentHandler.Describe(new TimeoutException("to rana@example.jo")));
    }

    /// <summary>A document issued from a fixture page's stored snapshot, as issuing would have stored it.</summary>
    private static FinancialDocument FromFixture(string name, int? schemaVersion = null)
    {
        var (_, page) = DocumentPrintLayoutTests.FixturePages().First(entry => entry.Name == name);
        var type = Enumeration.FromName<FinancialDocumentType>(Text(page, "type"));
        var version = page["version"]!.GetValue<int>();
        var (bookingId, paymentId, refundId) = (Id.New(), Id.New(), Id.New());
        var subject = type == FinancialDocumentType.PaymentReceipt ? paymentId : type == FinancialDocumentType.RefundReceipt ? refundId : bookingId;
        var issuedAt = DateTimeOffset.Parse(page["issuedAt"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture);
        var draft = new FinancialDocumentDraft(
            type,
            subject,
            Version: version,
            PreviousVersionId: version == 1 ? null : Id.New(),
            RelatedDocumentId: null,
            BookingId: bookingId,
            BookingReference: Text(page, "bookingReference"),
            CustomerId: Id.New(),
            DealerId: Id.New(),
            PaymentId: type == FinancialDocumentType.BookingStatement ? null : paymentId,
            RefundId: type == FinancialDocumentType.RefundReceipt ? refundId : null,
            Enumeration.FromName<FinancialDocumentCause>(Text(page, "cause")),
            OccurredAt: issuedAt,
            CoversThrough: null,
            CheckpointFingerprint: null,
            HeadlineAmount: Money.Jod(1m),
            Provider: PaymentProviders.Sandbox,
            CalculatorVersion: 1,
            SnapshotSchemaVersion: schemaVersion ?? page["snapshotSchemaVersion"]!.GetValue<int>(),
            Snapshot: page["snapshot"]!.ToJsonString());
        return FinancialDocument.Issue(draft, Text(page, "number"), issuedAt);
    }

    private static string Text(JsonNode page, string key) => page[key]!.GetValue<string>();
}
