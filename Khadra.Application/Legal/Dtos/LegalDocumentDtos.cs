namespace Khadra.Application.Legal.Dtos;

// The legal texts on the wire (Wave 2 G1). The public copy carries the HTML of the version in force and nothing about
// who published it. The administrator's copy adds the stored Markdown, the hashes and the publisher.

/// <summary>One value per language. Every legal text is published in both.</summary>
public sealed record LegalTextsDto(string En, string Ar);

/// <summary>Which version is which, as the administrator's list reads them.</summary>
public static class LegalVersionStates
{
    /// <summary>The version in force.</summary>
    public const string Current = "Current";

    /// <summary>A later version replaced it. Kept for good: it is what people were shown while it was in force.</summary>
    public const string Superseded = "Superseded";
}

/// <param name="Kind"><c>Terms</c> or <c>Privacy</c>.</param>
/// <param name="PublishedByName">Read live; null once the publisher's account no longer resolves.</param>
/// <param name="State"><see cref="LegalVersionStates"/>.</param>
public sealed record LegalDocumentVersionSummaryDto(
    Guid VersionId,
    string Kind,
    string VersionLabel,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset PublishedAt,
    Guid PublishedByAdminId,
    string? PublishedByName,
    string State);

/// <summary>One version in full, for the administrator.</summary>
/// <param name="Body">The Markdown as published, with LF line ends.</param>
/// <param name="Html">What the public page shows.</param>
/// <param name="Sha256">SHA-256 of each body's UTF-8 bytes: <c>sha256sum</c> of the same file gives the same.</param>
public sealed record LegalDocumentVersionDto(
    LegalDocumentVersionSummaryDto Summary,
    LegalTextsDto Body,
    LegalTextsDto Html,
    LegalTextsDto Sha256);

/// <summary>What publishing would publish, rendered by the renderer the public page uses. Nothing is written.</summary>
/// <param name="VersionLabel">The label as it would be stored: trimmed.</param>
/// <param name="Replaces">The version in force now, which this one would replace; null for the first.</param>
public sealed record LegalDocumentPreviewDto(
    string Kind,
    string VersionLabel,
    LegalTextsDto Html,
    LegalDocumentVersionSummaryDto? Replaces);

/// <summary>The version of a document in force, as anyone may read it.</summary>
public sealed record PublicLegalDocumentDto(
    string Kind,
    Guid VersionId,
    string VersionLabel,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset PublishedAt,
    LegalTextsDto Html);

/// <summary>The public read and its validator.</summary>
/// <param name="ETag">Names the version and the renderer, so it changes with either.</param>
/// <param name="Document">Null when the caller's copy is still the one in force (a 304).</param>
public sealed record CurrentLegalDocumentResult(string ETag, PublicLegalDocumentDto? Document);
