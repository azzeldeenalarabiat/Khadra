using System.Net;
using System.Text;
using System.Text.Json;
using Khadra.Application.FinancialDocuments.Rendering;
using Khadra.Domain.Common;
using Khadra.Domain.FinancialDocuments;

namespace Khadra.Application.FinancialDocuments.Email;

/// <summary>The words of one receipt's email, before its PDFs are attached (payments Phase 7).</summary>
public sealed record FinancialDocumentEmailText(string Subject, string HtmlBody, string TextBody);

/// <summary>
/// Lays out a receipt's email (payments Phase 7; owner, 2026-09-29): its key facts in the customer's language — or
/// Arabic then English for a customer who never chose — and the sentence that says its PDF, or its two PDFs, are
/// attached and where every receipt stays: in the customer's Khadra account, which the website and the app both open.
/// </summary>
/// <remarks>
/// <para>
/// <b>The record's words and figures, as its PDF prints them.</b> The title, the number, the headline and its amount
/// come through <see cref="DocumentPrintLayout"/>, which reads the stored snapshot exactly as the PDF does — so the
/// email can never say what the attached file does not, and a snapshot the layout refuses is not emailed at all. The
/// issue time is the frozen Amman wall time the snapshot holds, and a correction names the number it corrects from the
/// same header. The booking reference is the row's. Only the sentences around them are the email's own.
/// </para>
/// <para>
/// <b>No link while there is nowhere to send people</b> (<c>App:CustomerAppBaseUrl</c> empty, pre-launch item 91).
/// Once there is, each language's section links the website's own page for the document in that language
/// (<c>/{ar|en}/invoices/{id}</c>), which the app claims through its App Links.
/// </para>
/// </remarks>
public static class FinancialDocumentEmailLayout
{
    /// <summary>The email's words, or null when the stored document cannot be laid out — which its PDF could not be either.</summary>
    public static FinancialDocumentEmailText? TryCompose(
        FinancialDocument document,
        string customerName,
        IReadOnlyList<Language> languages,
        string? customerBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(customerName);
        ArgumentNullException.ThrowIfNull(languages);
        if (languages.Count == 0)
            throw new ArgumentException("An email is written in at least one language.", nameof(languages));

        var (issuedAtLocal, corrects) = Header(document.Snapshot);
        // One PDF per language the email is written in: both, for a customer who never chose.
        var bothPdfs = languages.Count > 1;
        var sections = new List<Section>();
        foreach (var language in languages)
        {
            var printed = DocumentPrintLayout.TryLayOut(
                document.SnapshotSchemaVersion, document.Snapshot, document.Number, document.IsTest, language);
            if (printed is null)
                return null;
            sections.Add(Words(printed, document, customerName.Trim(), issuedAtLocal, corrects, bothPdfs, Link(customerBaseUrl, language, document.Id)));
        }

        var subject = sections.Count == 1
            ? sections[0].Subject
            : $"{string.Join(" · ", sections.Select(section => section.Title))} {document.Number}";
        const string Divider = """<hr style="border:none;border-top:1px solid #ddd;margin:20px 0">""";
        return new FinancialDocumentEmailText(
            subject,
            string.Join(Divider, sections.Select(section => section.Html)),
            string.Join("\n----------\n\n", sections.Select(section => section.Text)));
    }

    /// <summary>The website's page for the document in <paramref name="language"/>, or null while there is no customer host.</summary>
    public static string? Link(string? customerBaseUrl, Language language, Id documentId)
    {
        ArgumentNullException.ThrowIfNull(language);
        var root = customerBaseUrl?.Trim().TrimEnd('/');
        return string.IsNullOrEmpty(root) ? null : $"{root}/{language.Name}/invoices/{documentId.Value:D}";
    }

    private sealed record Section(string Title, string Subject, string Html, string Text);

    private static Section Words(
        PrintedDocument printed,
        FinancialDocument document,
        string name,
        string? issuedAtLocal,
        string? corrects,
        bool bothPdfs,
        string? link)
    {
        var arabic = printed.Language == Language.Arabic;
        var title = DocumentPrintLayout.WithoutIsolates(printed.Title);
        // In Arabic every Latin run carries its own direction, as on the page; in English there is nothing to isolate.
        string Run(string value) => arabic ? DocumentPrintLayout.LeftToRight(value) : value;
        var amount = arabic ? printed.HeadlineAmount : DocumentPrintLayout.WithoutIsolates(printed.HeadlineAmount);
        var label = DocumentPrintLayout.WithoutIsolates(printed.HeadlineLabel);
        var issued = issuedAtLocal is null
            ? null
            : arabic
                ? DocumentPrintLayout.Literal(DocumentPrintLayout.FormatFrozenTime(issuedAtLocal, printed.Language))
                : DocumentPrintLayout.FormatFrozenTime(issuedAtLocal, printed.Language);

        var facts = new List<string>
        {
            $"{title} {Run(document.Number)}",
            arabic ? $"الحجز {Run(document.BookingReference)}" : $"Booking {document.BookingReference}",
            $"{label}: {amount}",
        };
        if (issued is not null)
            facts.Add(arabic ? $"صدر في {issued}" : $"Issued {issued}");

        var correction = corrects is null
            ? null
            : arabic
                ? $"يصحّح هذا الإيصال {Run(corrects)} الذي أُلغي."
                : $"This receipt corrects {corrects}, which was voided.";
        var greeting = arabic ? $"مرحباً {name}،" : $"Hi {name},";
        // One PDF or both, said as it is (owner, 2026-09-29); and "your account", which the website and the app both open.
        var attached = (arabic, bothPdfs) switch
        {
            (true, false) => "إيصالك مرفق بصيغة PDF. تجده، مع كل إيصال قبله، في «الفواتير والإيصالات» في حسابك على خضرا.",
            (true, true) => "إيصالك مرفق في ملفَّي PDF، بالعربية والإنجليزية. تجده، مع كل إيصال قبله، في «الفواتير والإيصالات» في حسابك على خضرا.",
            (false, false) => "Your receipt is attached as a PDF. You can find it, and every receipt before it, in Invoices & Receipts in your Khadra account.",
            (false, true) => "Your receipt is attached as two PDFs, in Arabic and English. You can find it, and every receipt before it, in Invoices & Receipts in your Khadra account.",
        };
        var open = arabic ? "افتحه في خضرا" : "Open it in Khadra";

        var html = new StringBuilder();
        html.Append(arabic ? """<div dir="rtl" lang="ar" style="text-align:right">""" : """<div dir="ltr" lang="en">""");
        html.Append("<p>").Append(Html(greeting)).Append("</p>");
        html.Append("<p>").Append(string.Join("<br>", facts.Select(Html))).Append("</p>");
        if (correction is not null)
            html.Append("<p>").Append(Html(correction)).Append("</p>");
        html.Append("<p>").Append(Html(attached)).Append("</p>");
        if (link is not null)
            html.Append("<p><a href=\"").Append(Html(link)).Append("\">").Append(Html(open)).Append("</a></p>");
        html.Append("</div>");

        var text = new StringBuilder();
        text.Append(greeting).Append("\n\n").Append(string.Join('\n', facts)).Append("\n\n");
        if (correction is not null)
            text.Append(correction).Append("\n\n");
        text.Append(attached).Append('\n');
        if (link is not null)
            text.Append(open).Append(": ").Append(link).Append('\n');

        return new Section(title, $"{title} {document.Number}", html.ToString(), text.ToString());
    }

    /// <summary>
    /// The two facts the email reads from the snapshot's header: when the document was issued, frozen in Amman wall
    /// time, and — for a correction — the number it corrects. Either missing is a line left out, never a refusal: the
    /// content the PDF prints has already been read whole by the layout.
    /// </summary>
    private static (string? IssuedAtLocal, string? Corrects) Header(string snapshot)
    {
        try
        {
            using var json = JsonDocument.Parse(snapshot);
            if (!json.RootElement.TryGetProperty("document", out var header) || header.ValueKind != JsonValueKind.Object)
                return (null, null);

            var issued = header.TryGetProperty("issuedAt", out var at)
                && at.ValueKind == JsonValueKind.Object
                && at.TryGetProperty("local", out var local)
                && local.ValueKind == JsonValueKind.String
                    ? local.GetString()
                    : null;
            var corrects = header.TryGetProperty("isCorrection", out var flag)
                && flag.ValueKind == JsonValueKind.True
                && header.TryGetProperty("previous", out var previous)
                && previous.ValueKind == JsonValueKind.Object
                && previous.TryGetProperty("number", out var number)
                && number.ValueKind == JsonValueKind.String
                    ? number.GetString()
                    : null;
            return (string.IsNullOrWhiteSpace(issued) ? null : issued, string.IsNullOrWhiteSpace(corrects) ? null : corrects);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string Html(string value) => WebUtility.HtmlEncode(value);
}
