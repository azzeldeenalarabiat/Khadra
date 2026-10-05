using Khadra.Application.Common.Ports;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Khadra.Infrastructure.Legal;

/// <summary>
/// The legal texts' Markdown subset, on Markdig (Wave 2 G1; security-reviewed by the advisor, 2026-10-05).
/// </summary>
/// <remarks>
/// <para>
/// <b>A closed list of node types.</b> A text is parsed with HTML parsing ON, so raw HTML shows up as nodes to refuse
/// rather than quietly becoming literal text. Every node must then be one of the types below, or the text is refused,
/// and the line is named. Anything new a Markdig upgrade adds is refused until somebody decides to publish it.
/// </para>
/// <para>
/// <b>Links are checked on their DECODED target.</b> Markdig unescapes entities and backslashes in a destination, so
/// <c>java&amp;#x73;cript:</c> arrives here as <c>javascript:</c>. Autolinks are checked too: <c>DisableHtml</c> does not
/// disable them, and CommonMark accepts any scheme in angle brackets. A link may go to <c>https:</c>, to
/// <c>mailto:</c>, or to a path on this site (<c>/…</c>). <c>//host</c> and <c>/\host</c> both leave the site, and are
/// refused.
/// </para>
/// <para>
/// <b>Rendered with HTML parsing OFF</b> as a second layer. A text that passed the check contains no HTML, so both
/// parses agree on it. Clients add a third layer: Angular sanitises <c>innerHTML</c>.
/// </para>
/// <para>
/// <b>Nothing too deep for Markdig.</b> Markdig refuses Markdown nested beyond its own limits by throwing, in the parser
/// for blocks and in the HTML renderer for emphasis. The check runs both passes, so a text the public page could not
/// render is refused before it is published, never answered later with a 500 to every reader.
/// </para>
/// </remarks>
internal sealed class MarkdigLegalTextRenderer : ILegalTextRenderer
{
    /// <summary>Bumped whenever a text renders to different HTML; the golden test says when.</summary>
    public const int RendererVersion = 1;

    private const int DeepestHeading = 4;

    private static readonly MarkdownPipeline Checking = new MarkdownPipelineBuilder()
        .UsePreciseSourceLocation()
        .Build();

    private static readonly MarkdownPipeline Rendering = new MarkdownPipelineBuilder()
        .DisableHtml()
        .Build();

    public int Version => RendererVersion;

    public LegalTextProblem? Check(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        if (!Manageable(markdown, out var document))
            return new LegalTextProblem(FirstUnmanageableLine(markdown), LegalTextProblem.Nesting);

        foreach (var node in document.Descendants())
        {
            var reason = Refusal(node);
            if (reason is not null)
                return new LegalTextProblem(node.Line + 1, reason);
        }

        return null;
    }

    public string Render(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        return Markdown.ToHtml(markdown, Rendering);
    }

    /// <summary>
    /// Whether both passes take the text: Markdig throws <see cref="ArgumentException"/> on Markdown nested past its
    /// limits ("too deeply nested"), the only exception it raises for a string it was given.
    /// </summary>
    private static bool Manageable(string markdown, out MarkdownDocument document)
    {
        try
        {
            document = Markdown.Parse(markdown, Checking);
            _ = Markdown.ToHtml(markdown, Rendering);
            return true;
        }
        catch (ArgumentException)
        {
            document = null!;
            return false;
        }
    }

    /// <summary>
    /// The line a too-deep text first becomes unmanageable on, so the administrator can find it: the shortest prefix of
    /// whole lines that one of the passes refuses, found by halving. Only ever run on a text already refused, so its
    /// cost (a few dozen parses at most) is paid for pathological input alone.
    /// </summary>
    private static int FirstUnmanageableLine(string markdown)
    {
        var ends = new List<int>();
        for (var index = 0; index < markdown.Length; index++)
        {
            if (markdown[index] == '\n')
                ends.Add(index + 1);
        }

        if (ends.Count == 0 || ends[^1] != markdown.Length)
            ends.Add(markdown.Length);

        var low = 0;
        var high = ends.Count - 1;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (Manageable(markdown[..ends[middle]], out _))
                low = middle + 1;
            else
                high = middle;
        }

        return low + 1;
    }

    /// <summary>Why a node cannot be published, or null when it can.</summary>
    private static string? Refusal(MarkdownObject node) => node switch
    {
        HtmlBlock or HtmlInline => LegalTextProblem.Html,
        CodeBlock or CodeInline => LegalTextProblem.Code,
        LinkInline { IsImage: true } => LegalTextProblem.Image,
        LinkInline link => IsAllowedTarget(link.Url) ? null : LegalTextProblem.Link,
        AutolinkInline autolink => IsAllowedAutolink(autolink) ? null : LegalTextProblem.Link,
        LinkReferenceDefinition definition => IsAllowedTarget(definition.Url) ? null : LegalTextProblem.Link,
        HeadingBlock heading => heading.Level <= DeepestHeading ? null : LegalTextProblem.Heading,
        _ when Published.Contains(node.GetType()) => null,
        _ => LegalTextProblem.Unsupported,
    };

    /// <summary>The node types published as they are; links and headings are checked above.</summary>
    private static readonly HashSet<Type> Published =
    [
        typeof(ParagraphBlock),
        typeof(ListBlock),
        typeof(ListItemBlock),
        typeof(QuoteBlock),
        typeof(ThematicBreakBlock),
        typeof(LinkReferenceDefinitionGroup),
        typeof(ContainerInline),
        typeof(LiteralInline),
        typeof(EmphasisInline),
        typeof(LineBreakInline),
        typeof(HtmlEntityInline),
    ];

    private static bool IsAllowedAutolink(AutolinkInline autolink) =>
        autolink.IsEmail ? IsPlain(autolink.Url) : IsAllowedTarget(autolink.Url);

    /// <summary>
    /// A link target that stays on the site or goes where a legal text may send a reader: <c>https:</c> or
    /// <c>mailto:</c>. Judged on the decoded target.
    /// </summary>
    internal static bool IsAllowedTarget(string? url)
    {
        if (!IsPlain(url))
            return false;

        // A path on this site, and only a path: "//host" and "/\host" are both read by browsers as another host.
        if (url![0] == '/')
            return url.Length == 1 || (url[1] != '/' && url[1] != '\\');

        return Uri.TryCreate(url, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttps || absolute.Scheme == Uri.UriSchemeMailto);
    }

    /// <summary>Not empty, and no whitespace or control character anywhere in it.</summary>
    private static bool IsPlain(string? url) =>
        !string.IsNullOrEmpty(url) && !url.Any(character => char.IsControl(character) || char.IsWhiteSpace(character));
}
