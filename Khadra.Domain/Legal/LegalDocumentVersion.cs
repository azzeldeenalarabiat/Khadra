using System.Security.Cryptography;
using System.Text;
using CSharpFunctionalExtensions;
using Khadra.Domain.Common;

namespace Khadra.Domain.Legal;

/// <summary>
/// One published version of a legal text, the Terms of Service or the Privacy notice, in English and in Arabic
/// (Wave 2 G1; pre-launch item 224).
/// </summary>
/// <remarks>
/// <para>
/// <b>Append-only.</b> A published version is evidence of what people were shown, and from Wave 4 of what they agreed
/// to, so it is never updated and never deleted: the <see cref="IAppendOnly"/> guard in <c>SaveChanges</c> and a
/// database trigger per table refuse both. A correction is a new version.
/// </para>
/// <para>
/// <b>In force the moment it is published.</b> <see cref="EffectiveFrom"/> is a column of its own, and the database
/// holds it to <c>effective_from ≥ published_at</c>, but this slice never schedules. A version scheduled for later
/// would block every urgent correction until its date, and could not be stopped from entering force. Scheduling comes
/// back only together with a way to withdraw a scheduled version (the advisor's review, 2026-10-05).
/// </para>
/// <para>
/// <b>Newest wins.</b> Every version is later than the one before it, so the version in force is simply the latest. The
/// domain asks for the latest instant before it publishes. A unique index on (kind, effective_from) settles the race
/// between two administrators publishing in the same instant.
/// </para>
/// <para>
/// <b>Who published it</b> is the administrator's id alone. Their name is in the audit entry written in the same
/// transaction. A second append-only copy of a staff member's name would be item 18 twice over.
/// </para>
/// </remarks>
public sealed class LegalDocumentVersion : AggregateRoot, IAppendOnly
{
    public const int MaxLabelLength = 40;

    /// <summary>A guard, not a business figure: real Terms run 20,000 to 60,000 characters.</summary>
    public const int MaxBodyLength = 200_000;

    /// <summary>A SHA-256 in lowercase hexadecimal.</summary>
    public const int HashLength = 64;

    private LegalDocumentVersion()
    {
    }

    private LegalDocumentVersion(Id id) : base(id)
    {
    }

    public LegalDocumentKind Kind { get; private set; } = null!;

    /// <summary>The administrator's name for the version, as published: "2026-10", "1.1". Unique per kind.</summary>
    public string VersionLabel { get; private set; } = null!;

    /// <summary>The instant the version came into force. Today always <see cref="PublishedAt"/>.</summary>
    public DateTimeOffset EffectiveFrom { get; private set; }

    public DateTimeOffset PublishedAt { get; private set; }

    public Id PublishedByAdminId { get; private set; }

    /// <summary>The English text, in the published Markdown subset, with LF line ends.</summary>
    public string BodyEn { get; private set; } = null!;

    /// <summary>The Arabic text, in the published Markdown subset, with LF line ends.</summary>
    public string BodyAr { get; private set; } = null!;

    /// <summary>
    /// SHA-256 of the UTF-8 bytes of <see cref="BodyEn"/>, so a file approved outside this system can be checked
    /// against what was published with <c>sha256sum</c>. Kept per language because a consent names the language
    /// the person read.
    /// </summary>
    public string BodyEnSha256 { get; private set; } = null!;

    /// <summary>SHA-256 of the UTF-8 bytes of <see cref="BodyAr"/>.</summary>
    public string BodyArSha256 { get; private set; } = null!;

    /// <summary>Publishes a version, in force from <paramref name="now"/>.</summary>
    /// <param name="latestEffectiveFrom">
    /// When the newest version of the same kind came into force, or null if there is none. The new one must be later,
    /// so the version in force is always the newest.
    /// </param>
    public static Result<LegalDocumentVersion, Error> Publish(
        LegalDocumentKind kind,
        string? versionLabel,
        string? bodyEn,
        string? bodyAr,
        Id publishedByAdminId,
        DateTimeOffset now,
        DateTimeOffset? latestEffectiveFrom)
    {
        ArgumentNullException.ThrowIfNull(kind);
        if (publishedByAdminId.IsEmpty)
            throw new DomainException("A legal document is published by an administrator.");

        var label = Label(versionLabel);
        if (label.IsFailure)
            return label.Error;
        var english = Body(bodyEn);
        if (english.IsFailure)
            return english.Error;
        var arabic = Body(bodyAr);
        if (arabic.IsFailure)
            return arabic.Error;
        if (latestEffectiveFrom is { } latest && now <= latest)
            return LegalErrors.PublishConflict;

        var instant = now.ToUniversalTime();
        return new LegalDocumentVersion(Id.New())
        {
            Kind = kind,
            VersionLabel = label.Value,
            EffectiveFrom = instant,
            PublishedAt = instant,
            PublishedByAdminId = publishedByAdminId,
            BodyEn = english.Value,
            BodyAr = arabic.Value,
            BodyEnSha256 = Sha256Hex(english.Value),
            BodyArSha256 = Sha256Hex(arabic.Value),
        };
    }

    /// <summary>A label as it is stored: trimmed, 1 to 40 characters, no control characters.</summary>
    public static Result<string, Error> Label(string? label)
    {
        var trimmed = label?.Trim() ?? string.Empty;
        return trimmed.Length is 0 or > MaxLabelLength || trimmed.Any(char.IsControl)
            ? LegalErrors.LabelInvalid
            : trimmed;
    }

    /// <summary>
    /// A text as it is stored: CRLF and lone CR become LF, so the hash matches the same file saved anywhere.
    /// </summary>
    /// <remarks>
    /// Refused rather than cleaned:
    /// <list type="bullet">
    /// <item>a text with nothing but whitespace;</item>
    /// <item>one longer than <see cref="MaxBodyLength"/>;</item>
    /// <item>any control character but a line break or a tab, NUL included, which PostgreSQL <c>text</c> cannot
    /// hold at all;</item>
    /// <item>a broken UTF-16 surrogate, which has no UTF-8 form to hash.</item>
    /// </list>
    /// Published text is exactly the text submitted, or nothing.
    /// </remarks>
    public static Result<string, Error> Body(string? body)
    {
        var text = (body ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        if (string.IsNullOrWhiteSpace(text))
            return LegalErrors.BodyRequired;
        if (text.Length > MaxBodyLength)
            return LegalErrors.BodyTooLong;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (char.IsControl(character) && character is not '\n' and not '\t')
                return LegalErrors.BodyInvalidCharacters;
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= text.Length || !char.IsLowSurrogate(text[index + 1]))
                    return LegalErrors.BodyInvalidCharacters;
                index++;
            }
            else if (char.IsLowSurrogate(character))
            {
                return LegalErrors.BodyInvalidCharacters;
            }
        }

        return text;
    }

    /// <summary>SHA-256 of a text's UTF-8 bytes, in lowercase hexadecimal: what <c>sha256sum</c> prints.</summary>
    public static string Sha256Hex(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
