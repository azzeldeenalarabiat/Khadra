using Khadra.Application.Common.Ports;
using Khadra.Infrastructure.Legal;

namespace Khadra.Tests.Infrastructure;

/// <summary>
/// The legal texts' Markdown subset (Wave 2 G1; security-reviewed by the advisor, 2026-10-05). Admin-written text
/// reaches every anonymous visitor as HTML. Anything outside a closed list is refused, with its line, and never
/// quietly cleaned.
/// </summary>
public sealed class LegalTextRendererTests
{
    private static readonly MarkdigLegalTextRenderer Renderer = new();

    private static LegalTextProblem? Check(string markdown) => Renderer.Check(markdown);

    // ── What is published ───────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("A paragraph with *emphasis*, **strength** and a line\\\nbreak.")]
    [InlineData("# One\n## Two\n### Three\n#### Four")]
    [InlineData("Setext\n======")]
    [InlineData("- one\n- two\n\n1. first\n2. second")]
    [InlineData("> A quoted passage.")]
    [InlineData("Above\n\n---\n\nBelow")]
    [InlineData("See [the privacy notice](/en/privacy), [our site](https://khadra.jo/) or [write](mailto:legal@khadra.jo).")]
    [InlineData("Home is [here](/).")]
    [InlineData("Write to <legal@khadra.jo> or visit <https://khadra.jo/help>.")]
    [InlineData("A [reference][terms] link.\n\n[terms]: https://khadra.jo/en/terms \"Terms\"")]
    [InlineData("Literal &lt;script&gt; stays literal, as does &amp; and &copy;.")]
    [InlineData("# الشروط والأحكام\n\nمرحبًا بك في **خضرا**. راجع [إشعار الخصوصية](/ar/privacy).")]
    public void Published_markdown_passes(string markdown) => Assert.Null(Check(markdown));

    // ── What is refused, and where ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("<div>Raw HTML.</div>", 1, "html")]
    [InlineData("Fine.\n\nAn <b>inline</b> tag.", 3, "html")]
    [InlineData("Fine.\n\n<!-- a comment -->", 3, "html")]
    [InlineData("![A picture](https://khadra.jo/logo.png)", 1, "image")]
    [InlineData("Text with `code` in it.", 1, "code")]
    [InlineData("Fine.\n\n    four spaces make code", 3, "code")]
    [InlineData("```\nfenced\n```", 1, "code")]
    [InlineData("##### Too deep", 1, "heading")]
    public void Anything_outside_the_subset_is_refused_with_its_line(string markdown, int line, string reason) =>
        Assert.Equal(new LegalTextProblem(line, reason), Check(markdown));

    /// <summary>Links are judged on their DECODED target: an entity or a backslash cannot smuggle a scheme in.</summary>
    [Theory]
    [InlineData("[x](javascript:alert(1))")]
    [InlineData("[x](java&#x73;cript:alert(1))")]
    [InlineData("[x](JAVASCRIPT:alert(1))")]
    [InlineData("[x](data:text/html,hi)")]
    [InlineData("[x](http://khadra.jo/)")]
    [InlineData("[x](//evil.example/x)")]
    [InlineData("[x](/\\evil.example/x)")]
    [InlineData("[x](#top)")]
    [InlineData("[x](privacy)")]
    [InlineData("[x](?page=1)")]
    [InlineData("[x](<>)")]
    [InlineData("[x](<https://khadra.jo/a b>)")]
    public void A_link_anywhere_but_https_mailto_or_a_page_on_this_site_is_refused(string markdown) =>
        Assert.Equal(LegalTextProblem.Link, Check(markdown)?.Reason);

    /// <summary>DisableHtml does not disable autolinks, and CommonMark takes any scheme between angle brackets.</summary>
    [Theory]
    [InlineData("<javascript:alert(1)>")]
    [InlineData("<vbscript:msgbox(1)>")]
    [InlineData("<http://khadra.jo>")]
    public void An_autolink_is_held_to_the_same_rule(string markdown) =>
        Assert.Equal(LegalTextProblem.Link, Check(markdown)?.Reason);

    [Fact]
    public void A_reference_definition_is_held_to_the_same_rule() =>
        Assert.Equal(LegalTextProblem.Link, Check("A [bad][x] link.\n\n[x]: javascript:alert(1)")?.Reason);

    [Fact]
    public void A_refusal_names_the_first_problem_and_its_line()
    {
        var text = "# Terms\n\nAll fine here.\n\nStill fine.\n\n![logo](https://khadra.jo/l.png)\n\n<div>later</div>";

        Assert.Equal(new LegalTextProblem(7, LegalTextProblem.Image), Check(text));
    }

    // ── The HTML itself ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Golden: a renderer change that alters published HTML must be seen and deliberate. When it is, bump
    /// <see cref="MarkdigLegalTextRenderer.RendererVersion"/>, which changes the public ETag, and update this.
    /// </summary>
    [Fact]
    public void The_html_of_a_published_text_is_pinned()
    {
        const string markdown =
            "# Terms of Service\n" +
            "\n" +
            "Welcome to **Khadra**. Read the [privacy notice](/en/privacy) or write to <legal@khadra.jo>.\n" +
            "\n" +
            "1. First\n" +
            "2. Second\n" +
            "\n" +
            "> Quoted &lt;b&gt;.\n" +
            "\n" +
            "---\n";

        Assert.Null(Check(markdown));
        Assert.Equal(
            "<h1>Terms of Service</h1>\n" +
            "<p>Welcome to <strong>Khadra</strong>. Read the <a href=\"/en/privacy\">privacy notice</a> or write to <a href=\"mailto:legal@khadra.jo\">legal@khadra.jo</a>.</p>\n" +
            "<ol>\n" +
            "<li>First</li>\n" +
            "<li>Second</li>\n" +
            "</ol>\n" +
            "<blockquote>\n" +
            "<p>Quoted &lt;b&gt;.</p>\n" +
            "</blockquote>\n" +
            "<hr />\n",
            Renderer.Render(markdown));
        Assert.Equal(1, Renderer.Version);
    }

    /// <summary>The second layer: even a text that skipped the check renders no live HTML.</summary>
    [Fact]
    public void Rendering_never_passes_raw_html_through()
    {
        var html = Renderer.Render("<script>alert(1)</script>\n\nAn <img src=x onerror=alert(1)> tag.");

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_arabic_text_renders_without_a_direction_of_its_own()
    {
        var html = Renderer.Render("# الشروط\n\nنص.");

        Assert.Equal("<h1>الشروط</h1>\n<p>نص.</p>\n", html);
        Assert.DoesNotContain("dir=", html, StringComparison.Ordinal);
    }
}
