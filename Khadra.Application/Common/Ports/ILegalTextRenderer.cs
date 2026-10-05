namespace Khadra.Application.Common.Ports;

/// <summary>
/// The Markdown subset legal texts are published in (Wave 2 G1). A text is checked before it is published and
/// rendered to HTML whenever it is read. There is one implementation, so the administrator's preview and the public
/// page come from the same renderer.
/// </summary>
/// <remarks>
/// <para>
/// <b>Published:</b> paragraphs, headings down to level 4, emphasis, lists, block quotes, thematic breaks, and links.
/// A link may go only to <c>https:</c>, <c>mailto:</c> or a page on this site (<c>/…</c>).
/// </para>
/// <para>
/// <b>Refused, not stripped:</b> raw HTML, images, code, any other link, and anything else. The version an
/// administrator previews is then exactly the version published. Silently removing part of a legal text would
/// publish words nobody read.
/// </para>
/// <para>
/// The stored Markdown is the record of what was published. The HTML is derived from it at read time and carries no
/// direction: a client sets <c>dir</c> and <c>lang</c> on the element that holds it.
/// </para>
/// </remarks>
public interface ILegalTextRenderer
{
    /// <summary>
    /// The renderer's own version, bumped whenever a text would render to different HTML. It is part of the public
    /// validator (ETag), so a client holding an old copy is sent the new one.
    /// </summary>
    int Version { get; }

    /// <summary>The first thing in the text that cannot be published, or null when there is none.</summary>
    LegalTextProblem? Check(string markdown);

    /// <summary>The text as HTML. Only ever called on a text <see cref="Check"/> accepts.</summary>
    string Render(string markdown);
}

/// <param name="Line">The line it is on, from 1.</param>
/// <param name="Reason">
/// A stable code a console words: <c>html</c>, <c>image</c>, <c>link</c>, <c>heading</c>, <c>code</c> or
/// <c>unsupported</c>.
/// </param>
public sealed record LegalTextProblem(int Line, string Reason)
{
    public const string Html = "html";
    public const string Image = "image";
    public const string Link = "link";
    public const string Heading = "heading";
    public const string Code = "code";
    public const string Unsupported = "unsupported";
}
