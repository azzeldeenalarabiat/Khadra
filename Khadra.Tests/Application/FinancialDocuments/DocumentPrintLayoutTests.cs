using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Khadra.Application.FinancialDocuments.Rendering;
using Khadra.Domain.Common;
using Khadra.Tests.Support;

namespace Khadra.Tests.Application.FinancialDocuments;

/// <summary>
/// Laying an issued document out for print (payments Phase 6): the fourth reader of the snapshot contract in
/// <c>docs/contracts/README.md</c>, proven against the same shared fixture the website, the app and the console
/// read — every page whole, in both languages, formatted and isolated as the website formats and isolates a
/// document, less the commercial registrations its body leaves out (owner, 2026-09-29).
/// </summary>
public sealed class DocumentPrintLayoutTests
{
    private const string Lri = "\u2066";
    private const string Fsi = "\u2068";
    private const string Pdi = "\u2069";

    private static readonly Language English = Language.English;
    private static readonly Language Arabic = Language.Arabic;
    private static readonly Language[] BothLanguages = [Language.English, Language.Arabic];
    private static readonly string[] ValueKinds = ["money", "instant", "text", "plain"];

    // ── Every document the server composes ──────────────────────────────────────────────────────────

    [Fact]
    public void Every_fixture_page_lays_out_whole_in_both_languages_with_its_sections_in_their_stored_order()
    {
        foreach (var (name, page) in FixturePages())
        {
            var content = page["snapshot"]!["content"]!;
            var stored = content["sections"]!.AsArray();
            foreach (var language in BothLanguages)
            {
                var printed = LayOut(page, language);
                Assert.True(printed is not null, $"{name} ({language.Name}) was refused.");
                Assert.Equal(stored.Count, printed!.Sections.Count);
                for (var index = 0; index < stored.Count; index++)
                    Assert.Equal(InBody(stored[index]!).Count, printed.Sections[index].Lines.Count);
                Assert.Equal(content["title"]![language.Name]!.GetValue<string>(), printed.Title);
            }
        }
    }

    [Fact]
    public void Amounts_and_literals_carry_their_own_direction_and_the_composers_sentences_keep_theirs()
    {
        foreach (var (name, page) in FixturePages())
        {
            var stored = page["snapshot"]!["content"]!["sections"]!.AsArray();
            foreach (var language in BothLanguages)
            {
                var printed = LayOut(page, language)!;
                for (var section = 0; section < stored.Count; section++)
                {
                    var lines = InBody(stored[section]!);
                    for (var index = 0; index < lines.Count; index++)
                    {
                        var line = lines[index];
                        var value = printed.Sections[section].Lines[index].Value;
                        if (line["money"] is not null)
                            Assert.True(value.StartsWith(Lri, StringComparison.Ordinal) && value.EndsWith(Pdi, StringComparison.Ordinal), $"{name}: an amount is not isolated left to right.");
                        else if (line["plain"]?.GetValue<string>() is { } plain)
                            Assert.Equal(DocumentPrintLayout.ContainsRightToLeftLetter(plain) ? Fsi + plain + Pdi : Lri + plain + Pdi, value);
                        else if (line["text"] is { } text)
                            Assert.Equal(text[language.Name]!.GetValue<string>(), value);
                    }
                }
            }
        }
    }

    [Fact]
    public void The_notice_that_it_is_not_a_tax_invoice_and_the_time_note_are_printed_in_the_pages_language()
    {
        var (_, page) = FixturePages().First(entry => entry.Name == "payment-receipt-paid-in-full");

        var english = LayOut(page, English)!;
        var arabic = LayOut(page, Arabic)!;

        Assert.Equal("This document is not a tax invoice.", english.Notice);
        Assert.Equal("هذا المستند ليس فاتورة ضريبية.", arabic.Notice);
        Assert.Equal("All times are Amman time.", english.TimeNote);
        Assert.Equal("جميع الأوقات بتوقيت عمّان.", arabic.TimeNote);
        Assert.Equal(Lri + "JOD 94.500" + Pdi, english.HeadlineAmount);
        Assert.Equal(Lri + "94.500 JOD" + Pdi, arabic.HeadlineAmount);
    }

    [Fact]
    public void A_test_document_is_watermarked_in_its_pages_language_and_a_real_one_is_not()
    {
        var (_, page) = FixturePages().First();
        var snapshot = page["snapshot"]!.ToJsonString();

        Assert.Equal("TEST", DocumentPrintLayout.TryLayOut(1, snapshot, "TEST-PAY-2026-000001", true, English)!.Watermark);
        Assert.Equal("تجريبي", DocumentPrintLayout.TryLayOut(1, snapshot, "TEST-PAY-2026-000001", true, Arabic)!.Watermark);
        Assert.Null(DocumentPrintLayout.TryLayOut(1, snapshot, "PAY-2026-000001", false, English)!.Watermark);
    }

    [Fact]
    public void The_file_is_titled_by_the_number_authored_by_the_issuer_and_dated_by_its_issue_with_no_direction_marks()
    {
        var (_, page) = FixturePages().First(entry => entry.Name == "booking-statement-dispute-decided");
        var printed = LayOut(page, Arabic)!;

        Assert.StartsWith(page["number"]!.GetValue<string>() + " — ", printed.Metadata.Title, StringComparison.Ordinal);
        Assert.Equal("ar", printed.Metadata.Language);
        Assert.False(string.IsNullOrWhiteSpace(printed.Metadata.Author));
        Assert.DoesNotContain(printed.Metadata.Title + printed.Metadata.Author, character => character is >= '\u2066' and <= '\u2069');
        Assert.Equal(
            DateTimeOffset.Parse(page["snapshot"]!["document"]!["issuedAt"]!["utc"]!.GetValue<string>(), CultureInfo.InvariantCulture),
            printed.Metadata.IssuedAt);
    }

    // ── What the record keeps and the page leaves out (owner, 2026-09-29) ───────────────────────────

    [Fact]
    public void The_issuers_and_the_offices_commercial_registrations_stay_in_the_record_and_out_of_the_pdf_in_both_languages()
    {
        var labels = new[] { "Commercial registration", "السجل التجاري", "Rental office's commercial registration", "السجل التجاري لمكتب التأجير" };
        foreach (var (name, page) in FixturePages())
        {
            var snapshot = page["snapshot"]!;
            var issuer = snapshot["issuer"]!["commercialRegistration"]!.GetValue<string>();
            var office = snapshot["office"]!["commercialRegistration"]!.GetValue<string>();
            var parties = snapshot["content"]!["sections"]!.AsArray().Single(section => section!["key"]!.GetValue<string>() == "parties")!;
            // The record is untouched: both numbers are stored, as facts and as lines of the document.
            Assert.False(string.IsNullOrWhiteSpace(issuer));
            Assert.False(string.IsNullOrWhiteSpace(office));
            Assert.Equal(
                ["issuerRegistration", "officeRegistration"],
                parties["lines"]!.AsArray().Select(line => line!["key"]!.GetValue<string>()).Where(key => key.EndsWith("Registration", StringComparison.Ordinal)));

            foreach (var language in BothLanguages)
            {
                var printed = LayOut(page, language)!;
                var everything = printed.Sections.SelectMany(section => section.Lines).ToList();
                Assert.DoesNotContain(everything, line => line.Label is { } label && labels.Contains(label));
                Assert.DoesNotContain(everything, line => DocumentPrintLayout.WithoutIsolates(line.Value) is var value && (value == issuer || value == office));
                // The parties still name all three: issuer, customer and office.
                Assert.Equal(InBody(parties).Count, printed.Sections.Single(section => section.Heading == parties["heading"]![language.Name]!.GetValue<string>()).Lines.Count);
                Assert.True(InBody(parties).Count >= 6, $"{name}: the parties lost more than the registrations.");
            }
        }
    }

    [Fact]
    public void A_customers_commercial_registration_is_left_out_too_while_there_are_no_business_accounts()
    {
        var snapshot = BrokenSnapshot(content =>
        {
            var parties = content["sections"]!.AsArray().Single(section => section!["key"]!.GetValue<string>() == "parties")!;
            parties["lines"]!.AsArray().Add(new JsonObject
            {
                ["key"] = "customerRegistration",
                ["label"] = new JsonObject { ["en"] = "Customer's commercial registration", ["ar"] = "السجل التجاري للعميل" },
                ["plain"] = "CR-TEST-7",
            });
        });

        foreach (var language in BothLanguages)
        {
            var printed = DocumentPrintLayout.TryLayOut(1, snapshot, "TEST-PAY-2026-000001", true, language)!;
            Assert.DoesNotContain(printed.Sections.SelectMany(section => section.Lines), line => line.Value.Contains("CR-TEST-7", StringComparison.Ordinal));
        }

        Assert.False(DocumentPrintLayout.IsPrintedInBody("parties", "customerRegistration"));
        // The rule is by section AND line: nothing else is left out.
        Assert.True(DocumentPrintLayout.IsPrintedInBody("booking", "officeRegistration"));
        Assert.True(DocumentPrintLayout.IsPrintedInBody("parties", "customer"));
    }

    [Fact]
    public void A_line_the_body_leaves_out_is_still_read_so_a_broken_one_refuses_the_document_whole()
    {
        var snapshot = BrokenSnapshot(content =>
        {
            var parties = content["sections"]!.AsArray().Single(section => section!["key"]!.GetValue<string>() == "parties")!;
            var registration = parties["lines"]!.AsArray().Single(line => line!["key"]!.GetValue<string>() == "issuerRegistration")!.AsObject();
            registration["text"] = new JsonObject { ["en"] = "a second value", ["ar"] = "قيمة ثانية" };
        });

        Assert.Null(Lay(snapshot));
    }

    // ── What is refused whole, and what is not ──────────────────────────────────────────────────────

    [Fact]
    public void A_schema_version_it_does_not_know_is_refused_and_nothing_is_printed()
    {
        var (_, page) = FixturePages().First();

        Assert.Null(DocumentPrintLayout.TryLayOut(2, page["snapshot"]!.ToJsonString(), "TEST-PAY-2026-000001", true, English));
        Assert.Null(DocumentPrintLayout.TryLayOut(1, "not json", "TEST-PAY-2026-000001", true, English));
        Assert.Null(DocumentPrintLayout.TryLayOut(1, "[]", "TEST-PAY-2026-000001", true, English));
    }

    [Theory]
    [InlineData("title")]
    [InlineData("headline")]
    [InlineData("sections")]
    public void A_structural_break_refuses_the_document_whole(string missing)
    {
        var snapshot = BrokenSnapshot(content => content.Remove(missing));

        Assert.Null(DocumentPrintLayout.TryLayOut(1, snapshot, "TEST-PAY-2026-000001", true, English));
    }

    [Fact]
    public void A_line_with_two_values_or_none_or_a_text_that_is_not_two_strings_refuses_the_document_whole()
    {
        Assert.Null(Lay(BrokenSnapshot(content =>
        {
            var line = FirstLine(content);
            var present = ValueKinds.First(kind => line[kind] is not null);
            line[ValueKinds.First(kind => kind != present)] = present == "plain"
                ? new JsonObject { ["amount"] = "1.000", ["currency"] = "JOD" }
                : "a second value";
        })));
        Assert.Null(Lay(BrokenSnapshot(content =>
        {
            var line = FirstLine(content);
            foreach (var kind in ValueKinds)
                line.Remove(kind);
        })));
        Assert.Null(Lay(BrokenSnapshot(content => content["title"]!["ar"] = 7)));
    }

    [Fact]
    public void Keys_it_does_not_know_are_ignored_anywhere()
    {
        var snapshot = BrokenSnapshot(content =>
        {
            content["layoutHint"] = "two-columns";
            FirstLine(content)["emphasis"] = "strong";
            content["sections"]![0]!["collapsed"] = true;
        });

        Assert.NotNull(Lay(snapshot));
    }

    // ── Figures and direction, as the website prints an issued document ─────────────────────────────

    [Theory]
    [InlineData("94.500", "en", "JOD 94.500")]
    [InlineData("94.500", "ar", "94.500 JOD")]
    [InlineData("1234567.500", "en", "JOD 1,234,567.500")]
    [InlineData("0001234.5", "ar", "1,234.5 JOD")]
    [InlineData("-0.000", "en", "JOD 0.000")]
    [InlineData("-12.000", "en", "JOD -12.000")]
    [InlineData("0", "en", "JOD 0")]
    [InlineData("twelve", "en", "JOD twelve")]
    public void An_amount_is_its_stored_digits_grouped_with_its_own_currency(string amount, string language, string expected) =>
        Assert.Equal(expected, DocumentPrintLayout.FormatAmount(amount, "JOD", Language.FromName<Language>(language)));

    [Theory]
    [InlineData("2026-09-27 11:17", "en", "27 Sept 2026, 11:17")]
    [InlineData("2026-09-27 11:17", "ar", "27 أيلول 2026، 11:17")]
    [InlineData("2026-01-05 09:05", "en", "5 Jan 2026, 09:05")]
    [InlineData("2026-12-31 23:59", "ar", "31 كانون الأول 2026، 23:59")]
    [InlineData("2026-02-30 10:00", "en", "2026-02-30 10:00")]
    [InlineData("2026-13-01 10:00", "en", "2026-13-01 10:00")]
    [InlineData("27/09/2026 11:17", "ar", "27/09/2026 11:17")]
    public void A_time_is_the_frozen_Amman_wall_time_never_moved_through_a_zone(string local, string language, string expected) =>
        Assert.Equal(expected, DocumentPrintLayout.FormatFrozenTime(local, Language.FromName<Language>(language)));

    [Theory]
    [InlineData("Petra Rentals", false)]
    [InlineData("+962 6 000 0000", false)]
    [InlineData("12-34567", false)]
    [InlineData("أوتو رنت — Auto Rent Jordan LLC", true)]
    [InlineData("Auto Rent — أوتو رنت", true)]
    public void A_literal_is_isolated_by_its_first_strong_character_when_it_holds_an_Arabic_letter_and_left_to_right_otherwise(
        string literal,
        bool firstStrong)
    {
        Assert.Equal((firstStrong ? Fsi : Lri) + literal + Pdi, DocumentPrintLayout.Literal(literal));
        Assert.Equal(firstStrong, DocumentPrintLayout.ContainsRightToLeftLetter(literal));
    }

    [Fact]
    public void Direction_marks_are_removed_only_where_nothing_is_laid_out()
    {
        Assert.Equal("تم استرداد 94.500 JOD", DocumentPrintLayout.WithoutIsolates("تم استرداد " + Fsi + "94.500 JOD" + Pdi));
        Assert.Equal(Lri + "TEST-PAY-2026-000001" + Pdi, DocumentPrintLayout.LeftToRight("TEST-PAY-2026-000001"));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    internal static IEnumerable<(string Name, JsonNode Page)> FixturePages()
    {
        var fixture = JsonNode.Parse(File.ReadAllText(RepositoryRoot.File("docs", "contracts", "financial-documents-v1.json")))!;
        foreach (var entry in fixture["documents"]!.AsArray())
            yield return (entry!["name"]!.GetValue<string>(), entry["page"]!);
    }

    internal static PrintedDocument? LayOut(JsonNode page, Language language)
    {
        var number = page["number"]!.GetValue<string>();
        return DocumentPrintLayout.TryLayOut(
            page["snapshotSchemaVersion"]!.GetValue<int>(),
            page["snapshot"]!.ToJsonString(),
            number,
            number.StartsWith("TEST-", StringComparison.Ordinal),
            language);
    }

    private static PrintedDocument? Lay(string snapshot) =>
        DocumentPrintLayout.TryLayOut(1, snapshot, "TEST-PAY-2026-000001", true, English);

    private static string BrokenSnapshot(Action<JsonObject> breakIt)
    {
        var (_, page) = FixturePages().First(entry => entry.Name == "payment-receipt-paid-in-full");
        var snapshot = JsonNode.Parse(page["snapshot"]!.ToJsonString())!.AsObject();
        breakIt(snapshot["content"]!.AsObject());
        return snapshot.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    private static JsonObject FirstLine(JsonObject content) => content["sections"]![0]!["lines"]![0]!.AsObject();

    /// <summary>A stored section's lines that the PDF prints in its body, in their stored order.</summary>
    private static List<JsonObject> InBody(JsonNode section)
    {
        var key = section["key"]!.GetValue<string>();
        return
        [
            .. section["lines"]!.AsArray()
                .Select(line => line!.AsObject())
                .Where(line => DocumentPrintLayout.IsPrintedInBody(key, line["key"]!.GetValue<string>())),
        ];
    }
}
