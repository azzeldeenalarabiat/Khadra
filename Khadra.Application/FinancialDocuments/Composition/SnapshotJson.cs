using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;

namespace Khadra.Application.FinancialDocuments.Composition;

/// <summary>
/// The canonical form of a document snapshot (payments Phase 5): the exact text that is stored, hashed,
/// and later rendered by every client and by the Phase 6 PDF.
/// </summary>
/// <remarks>
/// <para>
/// Canonical by construction rather than by sorting afterwards: the composer writes every object's keys
/// in one fixed order (a <see cref="JsonObject"/> keeps insertion order), numbers never pass through a
/// culture, amounts are strings at <see cref="Money.MinorUnits"/> decimals, and instants are UTC to the
/// second beside their local wall-clock reading. The same facts therefore always give the same bytes,
/// which is what lets <c>content_sha256</c> prove what was issued.
/// </para>
/// <para>
/// Arabic is written as itself, not as <c>\u</c> escapes: the column is <c>json</c>, which keeps the
/// text exactly, and a person reading the table should be able to read the document.
/// </para>
/// </remarks>
internal static class SnapshotJson
{
    /// <summary>The snapshot schema this code writes. A stored snapshot is never migrated; readers support every version.</summary>
    /// <remarks>
    /// <b>Publish first.</b> Raise this only AFTER an app build that renders the new version is published and
    /// confirmed working against the live API — and the website and the console deployed with their readers.
    /// A new version changes no field of the DTO and fires no 426, so an installed app meets it as a document
    /// it cannot show; documents are permanent and issued on the server's own schedule. The rule, and what
    /// forces a new version, is in docs/contracts/README.md ("Issued financial documents: the reader's
    /// contract"); a new grammar is a new fixture file beside financial-documents-v1.json, never an edit to it.
    /// </remarks>
    public const int SchemaVersion = 1;

    // Qualified: inside this class, "Money" is the method below.
    private static readonly string AmountFormat =
        "F" + Khadra.Domain.Common.Money.MinorUnits.ToString(CultureInfo.InvariantCulture);

    private static readonly JsonSerializerOptions Canonical = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    public static string Serialize(JsonObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return root.ToJsonString(Canonical);
    }

    /// <summary>An amount as the snapshot writes it: invariant, at the platform's minor-unit scale.</summary>
    public static string Amount(decimal amount) =>
        amount.ToString(AmountFormat, CultureInfo.InvariantCulture);

    /// <summary>"102.750 JOD": how an amount reads inside a sentence, in either language.</summary>
    public static string AmountText(Money money)
    {
        ArgumentNullException.ThrowIfNull(money);
        return $"{Amount(money.Amount)} {money.CurrencyCode}";
    }

    public static JsonObject Money(Money money)
    {
        ArgumentNullException.ThrowIfNull(money);
        return new JsonObject
        {
            ["amount"] = Amount(money.Amount),
            ["currency"] = money.CurrencyCode,
        };
    }

    public static JsonObject? OptionalMoney(Money? money) => money is null ? null : Money(money);

    public static JsonObject Text(BilingualText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new JsonObject
        {
            ["en"] = text.En,
            ["ar"] = text.Ar,
        };
    }

    public static JsonObject? OptionalText(BilingualText? text) => text is null ? null : Text(text);

    /// <summary>An instant in UTC, to the second: <c>2026-09-25T17:56:13Z</c>.</summary>
    public static string Utc(DateTimeOffset at) =>
        at.UtcDateTime.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>The same instant on the reporting zone's wall clock, with Latin digits: <c>2026-09-25 20:56</c>.</summary>
    public static string Local(DateTimeOffset at, IReportingCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{calendar.DayOf(at):yyyy'-'MM'-'dd} {calendar.TimeOfDay(at):HH':'mm}");
    }

    public static JsonObject Instant(DateTimeOffset at, IReportingCalendar calendar) =>
        new()
        {
            ["utc"] = Utc(at),
            ["local"] = Local(at, calendar),
        };

    public static JsonObject? OptionalInstant(DateTimeOffset? at, IReportingCalendar calendar) =>
        at is { } instant ? Instant(instant, calendar) : null;

    public static JsonObject Reference(DocumentReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return new JsonObject
        {
            ["documentId"] = reference.DocumentId.Value.ToString("D", CultureInfo.InvariantCulture),
            ["number"] = reference.Number,
        };
    }

    public static string IdText(Id id) => id.Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>
/// A document laid out for reading (the snapshot's <c>content</c>): a title, a headline figure, sections of
/// labelled lines, and the notice. Every label is stored in English and Arabic, and every value is one of
/// four kinds — money, an instant, a worded text, or a plain literal such as a reference — so a renderer
/// needs no wording of its own and can compute nothing.
/// </summary>
internal sealed class DocumentContent
{
    private readonly JsonArray _sections = [];

    public DocumentSection Section(string key, BilingualText heading)
    {
        var section = new DocumentSection(key, heading);
        _sections.Add(section.Node);
        return section;
    }

    public JsonObject Build(BilingualText title, BilingualText headlineLabel, Money headline) =>
        new()
        {
            ["title"] = SnapshotJson.Text(title),
            ["headline"] = new JsonObject
            {
                ["label"] = SnapshotJson.Text(headlineLabel),
                ["money"] = SnapshotJson.Money(headline),
            },
            ["sections"] = _sections,
            ["timeNote"] = SnapshotJson.Text(DocumentWording.TimeNote),
            ["notice"] = SnapshotJson.Text(DocumentWording.NotATaxInvoice),
        };
}

/// <summary>One section of a document's content: a heading and its lines, in the order they are read.</summary>
internal sealed class DocumentSection
{
    private readonly JsonArray _lines = [];

    public DocumentSection(string key, BilingualText heading)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Node = new JsonObject
        {
            ["key"] = key,
            ["heading"] = SnapshotJson.Text(heading),
            ["lines"] = _lines,
        };
    }

    public JsonObject Node { get; }

    public int Count => _lines.Count;

    public DocumentSection Money(string key, BilingualText label, Money money) =>
        Add(key, label, "money", SnapshotJson.Money(money));

    public DocumentSection Instant(string key, BilingualText label, DateTimeOffset at, IReportingCalendar calendar) =>
        Add(key, label, "instant", SnapshotJson.Instant(at, calendar));

    /// <summary>A worded value — or, with no label, a sentence standing on its own.</summary>
    public DocumentSection Text(string key, BilingualText? label, BilingualText text) =>
        Add(key, label, "text", SnapshotJson.Text(text));

    /// <summary>A literal that reads the same in both languages: a reference, a number, a name as registered.</summary>
    public DocumentSection Plain(string key, BilingualText label, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Add(key, label, "plain", JsonValue.Create(value));
    }

    private DocumentSection Add(string key, BilingualText? label, string kind, JsonNode value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _lines.Add(new JsonObject
        {
            ["key"] = key,
            ["label"] = SnapshotJson.OptionalText(label),
            [kind] = value,
        });
        return this;
    }
}
