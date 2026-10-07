using Khadra.Domain.Common;

namespace Khadra.Domain.Legal;

public static class LegalErrors
{
    public static readonly Error KindUnknown =
        Error.NotFound("legal.kind_unknown", "There is no such legal document.");

    public static readonly Error NotPublished =
        Error.NotFound("legal.not_published", "No version of this document has been published yet.");

    public static readonly Error VersionNotFound =
        Error.NotFound("legal.version_not_found", "That version was not found.");

    public static readonly Error LabelInvalid =
        Error.Validation(
            "legal.label_invalid",
            $"A version label is required, at most {LegalDocumentVersion.MaxLabelLength} characters, with no control characters.");

    public static readonly Error LabelTaken =
        Error.Conflict("legal.label_taken", "A version of this document already has that label.");

    public static readonly Error BodyRequired =
        Error.Validation("legal.body_required", "Both the English and the Arabic text are required.");

    public static readonly Error BodyTooLong =
        Error.Validation("legal.body_too_long", $"A text can be at most {LegalDocumentVersion.MaxBodyLength} characters.");

    public static readonly Error BodyInvalidCharacters =
        Error.Validation(
            "legal.body_invalid_characters",
            "A text may contain no control characters other than line breaks and tabs, and no broken characters.");

    /// <summary>
    /// Another version of the document was published at the same instant or later, so this one would not be the newest.
    /// Ask again: the list has changed.
    /// </summary>
    public static readonly Error PublishConflict =
        Error.Conflict(
            "legal.publish_conflict",
            "Another version of this document was published at the same moment. Reload the list and try again.");

    /// <summary>
    /// A registration or an invitation that must accept the texts in force did not (Wave 4, W4-8). Only a caller that is
    /// not the customer app is asked, and only while a text is in force.
    /// </summary>
    public static readonly Error ConsentRequired =
        Error.Validation(
            "legal.consent_required",
            "Read and accept the Terms of Service and the Privacy notice to continue.");

    /// <summary>
    /// A consent named a version that is not the one in force — superseded since the screen read it, or never published.
    /// The screen reloads the texts and asks again.
    /// </summary>
    public static readonly Error VersionNotCurrent =
        Error.Conflict(
            "legal.version_not_current",
            "The Terms of Service or the Privacy notice have just been updated. Read the current version and accept it again.");

    /// <summary>An acceptance must say which language the texts were read in: the version holds both.</summary>
    public static readonly Error ConsentLanguageRequired =
        Error.Validation("legal.consent_language_required", "Say which language the texts were read in: ar or en.");

    /// <summary>
    /// The answer of the consent gate (Wave 4, W4-8): this account has not accepted a text in force, and the website or
    /// the console asked for something other than what resolves that. Never sent to the customer app or to an
    /// administrator.
    /// </summary>
    public static readonly Error ConsentPending =
        Error.Forbidden(
            "legal.consent_pending",
            "Accept the current Terms of Service and Privacy notice to continue.");

    /// <summary>
    /// The text uses Markdown this platform does not publish. Its extensions say which language, which line and why:
    /// <c>language</c> (<c>en</c> or <c>ar</c>), <c>line</c> (from 1) and <c>reason</c>.
    /// </summary>
    public static Error TextUnsupported(string language, int line, string reason) =>
        new(
            "legal.text_unsupported",
            $"The {(language == "ar" ? "Arabic" : "English")} text uses something that cannot be published (line {line}: {reason}).",
            ErrorKind.Validation,
            Extensions: new Dictionary<string, object?>
            {
                ["language"] = language,
                ["line"] = line,
                ["reason"] = reason,
            });
}
