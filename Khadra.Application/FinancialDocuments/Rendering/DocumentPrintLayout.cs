using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Khadra.Domain.Common;

namespace Khadra.Application.FinancialDocuments.Rendering;

/// <summary>
/// Lays an issued document out for print in one language (payments Phase 6), reading its STORED snapshot
/// under the reader's contract in <c>docs/contracts/README.md</c> — exactly as the website, the app and the
/// console read it, so a PDF can never say what the document page does not. It is the contract's fourth
/// reader.
/// </summary>
/// <remarks>
/// <para>
/// It ignores keys it does not know and keeps sections and lines in their stored order; it refuses a
/// document WHOLE (answers null) on a structural break or a schema version it does not know, because a
/// permanent record printed with a line quietly missing is worse than none; and it prints an amount or a
/// time that is off its pattern as stored rather than dropping it.
/// </para>
/// <para>
/// One deliberate exception to "every line": the commercial registrations stay in the record and out of the
/// PDF's body (<see cref="IsPrintedInBody"/>; owner, 2026-09-29). That is a named presentation rule, read after
/// the line itself has been read whole — never a line lost to a snapshot this reader could not understand.
/// </para>
/// <para>
/// Figures are formatted the way the website formats an issued document (<c>FormatService.storedMoney</c>
/// and <c>frozenTime</c>, locales <c>en-GB</c> and <c>ar-JO-u-nu-latn</c>): an amount from its stored
/// string, grouped and never through a float, with its own currency code; a time as the frozen Amman wall
/// time the snapshot holds, never moved through a zone; Latin digits in both languages. The month names are
/// written out here, from those locales' CLDR data, so a PDF reads the same on every host whatever culture
/// data it carries.
/// </para>
/// <para>
/// Direction is carried by Unicode isolates, as the website carries it: the composer's own isolates around a
/// run inside a sentence (<c>DocumentWording.Isolate</c>) are kept as stored; an amount is isolated left to
/// right; a literal as registered is isolated by its first strong character when it holds an Arabic letter
/// (<c>&lt;bdi&gt;</c>) and left to right otherwise (<c>.ltr</c>). QuestPDF resolves isolates as the Unicode
/// bidi algorithm does — checked by eye on 2026-09-29, when an amount given only a span direction was
/// reversed inside an Arabic line and the same amount isolated was not — so the isolate, not a span's own
/// direction, is what keeps a figure in order.
/// </para>
/// </remarks>
public static partial class DocumentPrintLayout
{
    /// <summary>The snapshot schema versions a PDF is rendered from. A new one is added beside version 1.</summary>
    public static readonly IReadOnlyList<int> SupportedSchemaVersions = [1];

    /// <summary>
    /// The languages every document is drawn in, in the order a page offers them: what the settlement pass draws
    /// (<c>FinancialDocumentRenditionWorkReader</c>) and what a page counts before it says a PDF is still being
    /// prepared. Never the platform's whole list of languages, which other features may grow.
    /// </summary>
    public static readonly IReadOnlyList<Language> Languages = [Language.English, Language.Arabic];

    /// <summary>
    /// The version of what a PDF looks like. Raise it for ANY change to a rendition's appearance — the layout
    /// here or the page the renderer draws — so a new rendition is made beside the old one, for documents
    /// rendered from then on, rather than silently replacing what a customer may already have downloaded.
    /// </summary>
    public const int TemplateVersion = 1;

    private const char LeftToRightIsolate = '\u2066';
    private const char RightToLeftIsolate = '\u2067';
    private const char FirstStrongIsolate = '\u2068';
    private const char PopDirectionalIsolate = '\u2069';

    private static readonly string[] EnglishMonths =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sept", "Oct", "Nov", "Dec"];

    private static readonly string[] ArabicMonths =
    [
        "كانون الثاني", "شباط", "آذار", "نيسان", "أيار", "حزيران",
        "تموز", "آب", "أيلول", "تشرين الأول", "تشرين الثاني", "كانون الأول",
    ];

    private static readonly string[] ValueKinds = ["money", "instant", "text", "plain"];

    /// <summary>
    /// The document laid out in <paramref name="language"/>, or null when its snapshot cannot be printed
    /// whole: a schema version this renderer does not know, or a structural break.
    /// </summary>
    /// <param name="schemaVersion">The row's <c>snapshot_schema_version</c>, never the snapshot's own.</param>
    /// <param name="isTest">The row's own marker (<c>FinancialDocument.IsTest</c>), never the snapshot's.</param>
    public static PrintedDocument? TryLayOut(int schemaVersion, string snapshot, string number, bool isTest, Language language)
    {
        ArgumentNullException.ThrowIfNull(language);
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        if (!SupportedSchemaVersions.Contains(schemaVersion) || string.IsNullOrWhiteSpace(snapshot))
            return null;

        try
        {
            using var json = JsonDocument.Parse(snapshot);
            var root = Object(json.RootElement);
            var content = Object(Required(root, "content"));
            var headline = Object(Required(content, "headline"));
            var (amount, currency) = Money(Required(headline, "money"));
            var title = Words(Required(content, "title"), language);
            var sections = Array(Required(content, "sections"))
                .Select(section => Section(section, language))
                .ToList();
            var arabic = language == Language.Arabic;

            return new PrintedDocument(
                language,
                number,
                title,
                Words(Required(headline, "label"), language),
                LeftToRight(FormatAmount(amount, currency, language)),
                sections,
                OptionalWords(content, "timeNote", language),
                OptionalWords(content, "notice", language),
                isTest ? Watermark(language) : null,
                arabic ? "صفحة" : "Page",
                arabic ? "من" : "of",
                new PrintedMetadata($"{number} — {WithoutIsolates(title)}", IssuerName(root, language), language.Name, IssuedAt(root)));
        }
        catch (JsonException)
        {
            // Not JSON, or a structural break (Unreadable): the document is refused whole.
            return null;
        }
    }

    /// <summary>A TEST document's watermark, in the page's language: a fixed template word, never the record's.</summary>
    public static string Watermark(Language language)
    {
        ArgumentNullException.ThrowIfNull(language);
        return language == Language.Arabic ? "تجريبي" : "TEST";
    }

    /// <summary>
    /// An amount as the document stored it — its own digits, grouped, and its own currency code, placed as the
    /// website places it (<c>JOD 94.500</c> in English, <c>94.500 JOD</c> in Arabic). Text that is not an
    /// amount is printed as it is.
    /// </summary>
    public static string FormatAmount(string amount, string currency, Language language)
    {
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentNullException.ThrowIfNull(currency);
        ArgumentNullException.ThrowIfNull(language);

        var match = StoredAmount().Match(amount);
        var digits = amount;
        if (match.Success)
        {
            var (sign, whole, fraction) = (match.Groups[1].Value, match.Groups[2].Value, match.Groups[3]);
            // A stored "-0.000" is zero: no figure on the platform reads "-0" (the website's rule too).
            var negative = sign.Length > 0 && (whole + fraction.Value).Any(digit => digit is >= '1' and <= '9');
            digits = (negative ? "-" : string.Empty) + Grouped(whole) + (fraction.Success ? "." + fraction.Value : string.Empty);
        }

        return language == Language.Arabic ? $"{digits} {currency}" : $"{currency} {digits}";
    }

    /// <summary>
    /// A wall time an issued document froze in Amman (<c>2026-09-27 11:17</c>) as the website prints it:
    /// <c>27 Sept 2026, 11:17</c>, <c>27 أيلول 2026، 11:17</c>. Text that is not such a time is printed as it is.
    /// </summary>
    public static string FormatFrozenTime(string local, Language language)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(language);

        var match = FrozenLocal().Match(local);
        if (!match.Success)
            return local;
        var (year, month, day, hour, minute) = (
            Number(match.Groups[1]), Number(match.Groups[2]), Number(match.Groups[3]), Number(match.Groups[4]), Number(match.Groups[5]));
        // "2026-13-45 99:99" is not a time: a record prints as stored instead of rolling over into another date.
        if (year < 1 || month is < 1 or > 12)
            return local;
        if (day < 1 || day > DateTime.DaysInMonth(year, month) || hour > 23 || minute > 59)
            return local;

        var arabic = language == Language.Arabic;
        var clock = string.Create(CultureInfo.InvariantCulture, $"{hour:D2}:{minute:D2}");
        var date = string.Create(CultureInfo.InvariantCulture, $"{day} {(arabic ? ArabicMonths : EnglishMonths)[month - 1]} {year}");
        return arabic ? $"{date}، {clock}" : $"{date}, {clock}";
    }

    /// <summary>A run isolated left to right: an amount, a document number, a literal with no Arabic letter.</summary>
    public static string LeftToRight(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return $"{LeftToRightIsolate}{text}{PopDirectionalIsolate}";
    }

    /// <summary>
    /// A literal as registered — a name, a reference, a plate, a phone number — isolated as the website
    /// isolates it: by its first strong character when it holds an Arabic letter (<c>&lt;bdi&gt;</c>), so
    /// «أوتو رنت — Auto Rent Jordan LLC» reads from the right; left to right otherwise (<c>.ltr</c>), so
    /// <c>+962 6 000 0000</c> keeps its order on an Arabic page.
    /// </summary>
    public static string Literal(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return ContainsRightToLeftLetter(text) ? $"{FirstStrongIsolate}{text}{PopDirectionalIsolate}" : LeftToRight(text);
    }

    /// <summary>Whether a text holds a Hebrew or Arabic letter: the same class the three clients use.</summary>
    public static bool ContainsRightToLeftLetter(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Any(character =>
            character is >= '\u0590' and <= '\u08FF' or >= '\uFB1D' and <= '\uFDFF' or >= '\uFE70' and <= '\uFEFF');
    }

    /// <summary>The text with every direction isolate removed: for the file's metadata, which is not laid out.</summary>
    public static string WithoutIsolates(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return string.Concat(text.Where(character =>
            character is not (LeftToRightIsolate or RightToLeftIsolate or FirstStrongIsolate or PopDirectionalIsolate)));
    }

    /// <summary>
    /// Whether the PDF prints a line in its body. Every line is still READ first — a structural break in one
    /// that is left out refuses the document whole, as it would anywhere — and the snapshot keeps them all:
    /// this decides what the page shows, never what the record holds.
    /// </summary>
    /// <remarks>
    /// Left out (owner, 2026-09-29, presentation only): Khadra's commercial registration, the rental office's,
    /// and a customer's. A customer's would be printed only for a business account, which the platform does not
    /// have; when it does, that account is a fact the snapshot will carry, and this is where it is let back in.
    /// Should the law come to require Khadra's registration on a document, it goes in a compact legal line in the
    /// page's footer — never back among the transaction details.
    /// </remarks>
    public static bool IsPrintedInBody(string sectionKey, string lineKey) =>
        !(sectionKey == "parties" && lineKey is "issuerRegistration" or "officeRegistration" or "customerRegistration");

    private static PrintedSection Section(JsonElement value, Language language)
    {
        var node = Object(value);
        var key = String(Required(node, "key"));
        var heading = Words(Required(node, "heading"), language);
        var lines = new List<PrintedLine>();
        foreach (var line in Array(Required(node, "lines")))
        {
            var (lineKey, printed) = Line(line, language);
            if (IsPrintedInBody(key, lineKey))
                lines.Add(printed);
        }

        return new PrintedSection(heading, lines);
    }

    private static (string Key, PrintedLine Line) Line(JsonElement value, Language language)
    {
        var node = Object(value);
        var key = String(Required(node, "key"));
        var kinds = ValueKinds.Where(kind => Present(node, kind)).ToList();
        if (kinds.Count != 1)
            throw Unreadable();
        var label = Present(node, "label") ? Words(node.GetProperty("label"), language) : null;
        var raw = node.GetProperty(kinds[0]);

        switch (kinds[0])
        {
            case "money":
                var (amount, currency) = Money(raw);
                return (key, new PrintedLine(label, LeftToRight(FormatAmount(amount, currency, language))));
            case "instant":
                return (key, new PrintedLine(label, FormatFrozenTime(Instant(raw), language)));
            case "text":
                return (key, new PrintedLine(label, Words(raw, language)));
            default:
                return (key, new PrintedLine(label, Literal(String(raw))));
        }
    }

    private static (string Amount, string Currency) Money(JsonElement value)
    {
        var node = Object(value);
        return (String(Required(node, "amount")), String(Required(node, "currency")));
    }

    private static string Instant(JsonElement value)
    {
        var node = Object(value);
        String(Required(node, "utc"));
        return String(Required(node, "local"));
    }

    private static string? OptionalWords(JsonElement node, string name, Language language) =>
        Present(node, name) ? Words(node.GetProperty(name), language) : null;

    /// <summary>A text's words in the page's language, as stored — its isolates included. Both halves must be there.</summary>
    private static string Words(JsonElement value, Language language)
    {
        var node = Object(value);
        var english = String(Required(node, "en"));
        var arabic = String(Required(node, "ar"));
        return language == Language.Arabic ? arabic : english;
    }

    /// <summary>The issuer's registered name for the file's author: metadata only, never a reason to refuse.</summary>
    private static string? IssuerName(JsonElement root, Language language)
    {
        if (!root.TryGetProperty("issuer", out var issuer) || issuer.ValueKind != JsonValueKind.Object
            || !issuer.TryGetProperty("legalName", out var name) || name.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return name.TryGetProperty(language.Name, out var words) && words.ValueKind == JsonValueKind.String
            ? WithoutIsolates(words.GetString()!)
            : null;
    }

    private static DateTimeOffset? IssuedAt(JsonElement root) =>
        root.TryGetProperty("document", out var document) && document.ValueKind == JsonValueKind.Object
        && document.TryGetProperty("issuedAt", out var issuedAt) && issuedAt.ValueKind == JsonValueKind.Object
        && issuedAt.TryGetProperty("utc", out var utc) && utc.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(utc.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var instant)
            ? instant.ToUniversalTime()
            : null;

    private static bool Present(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

    private static JsonElement Required(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value) ? value : throw Unreadable();

    private static JsonElement Object(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object ? value : throw Unreadable();

    private static JsonElement.ArrayEnumerator Array(JsonElement value) =>
        value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : throw Unreadable();

    private static string String(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString()! : throw Unreadable();

    private static int Number(Group group) => int.Parse(group.Value, NumberStyles.None, CultureInfo.InvariantCulture);

    private static string Grouped(string whole)
    {
        var trimmed = whole.TrimStart('0');
        if (trimmed.Length == 0)
            return "0";
        var grouped = new StringBuilder(trimmed.Length + (trimmed.Length / 3));
        for (var index = 0; index < trimmed.Length; index++)
        {
            if (index > 0 && (trimmed.Length - index) % 3 == 0)
                grouped.Append(',');
            grouped.Append(trimmed[index]);
        }

        return grouped.ToString();
    }

    /// <summary>A structural break: the document is refused whole.</summary>
    private static JsonException Unreadable() => new("The snapshot does not follow the version 1 grammar.");

    // ASCII digits only, never \d: in .NET that is any Unicode decimal digit, which the ASCII-only parsing
    // after a match would then refuse with an exception rather than print the text as stored.
    [GeneratedRegex(@"^(-?)([0-9]+)(?:\.([0-9]+))?$", RegexOptions.CultureInvariant)]
    private static partial Regex StoredAmount();

    [GeneratedRegex(@"^([0-9]{4})-([0-9]{2})-([0-9]{2}) ([0-9]{2}):([0-9]{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex FrozenLocal();
}
