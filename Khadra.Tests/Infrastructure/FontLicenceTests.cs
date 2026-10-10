using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Khadra.Infrastructure.FinancialDocuments;
using Khadra.Tests.Support;

namespace Khadra.Tests.Infrastructure;

/// <summary>
/// The fonts' licences travel with the fonts (pre-launch item 196): the SIL Open Font License lets Manrope and Noto
/// Kufi Arabic be bundled and embedded as long as each copy carries the licence and the copyright notice. Both
/// places that bundle the files — the API assembly, for the PDFs, and the customer app — keep each family's
/// licence text, copied unchanged from its official repository, and a notice (<c>FONTS.md</c>) naming every font
/// file with its own copyright line and its hash. These hold the three to one another — the notice's rows read back
/// from each font file's own metadata — so a font replaced without its notice, or a family added without its
/// licence, fails here rather than ships.
/// </summary>
public sealed partial class FontLicenceTests
{
    private static readonly Dictionary<string, string> LicenceFiles = new(StringComparer.Ordinal)
    {
        ["Manrope"] = "OFL-Manrope.txt",
        ["Noto Kufi Arabic"] = "OFL-NotoKufiArabic.txt",
    };

    // The addresses a font names for the SIL Open Font License 1.1: SIL's old one and the licence's own site.
    private static readonly string[] OflAddresses = ["http://scripts.sil.org/OFL", "https://scripts.sil.org/OFL", "https://openfontlicense.org"];

    public static TheoryData<string> FontFolders => new()
    {
        "Khadra.Infrastructure/FinancialDocuments/Fonts",
        "Khadra.Mobile/assets/fonts",
    };

    [Theory]
    [MemberData(nameof(FontFolders))]
    public void Every_font_file_is_named_in_its_notice_with_its_own_hash_and_its_familys_licence_is_beside_it(string folder)
    {
        var directory = RepositoryRoot.File([.. folder.Split('/')]);
        var notice = ReadNotice(Path.Combine(directory, "FONTS.md"));
        var rows = NoticeRow().Matches(notice).ToDictionary(match => match.Groups["file"].Value, match => match.Groups["hash"].Value, StringComparer.Ordinal);
        var fonts = Directory.GetFiles(directory, "*.ttf").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();

        Assert.NotEmpty(fonts);
        Assert.Equal(fonts, rows.Keys.Order(StringComparer.Ordinal));
        foreach (var font in fonts)
            Assert.Equal(rows[font!], Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, font!)))));

        foreach (var (family, licence) in LicenceFiles)
        {
            var text = File.ReadAllText(Path.Combine(directory, licence));
            Assert.Contains("This Font Software is licensed under the SIL Open Font License, Version 1.1.", text, StringComparison.Ordinal);
            Assert.Contains("SIL OPEN FONT LICENSE Version 1.1 - 26 February 2007", text, StringComparison.Ordinal);
            Assert.Contains(family == "Manrope" ? "The Manrope Project Authors" : "The Noto Project Authors", text.Split('\n')[0], StringComparison.Ordinal);
            Assert.Contains($"`{licence}`", notice, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(FontFolders))]
    public void Every_font_files_own_metadata_says_what_its_notice_says_and_names_the_licence_bundled_beside_it(string folder)
    {
        var directory = RepositoryRoot.File([.. folder.Split('/')]);
        var rows = NoticeRow().Matches(ReadNotice(Path.Combine(directory, "FONTS.md")));

        Assert.NotEmpty(rows);
        foreach (Match row in rows)
        {
            var file = row.Groups["file"].Value;
            var names = NamesOf(File.ReadAllBytes(Path.Combine(directory, file)));

            // The notice reproduces the file's own words, not a paraphrase of them.
            Assert.Equal(row.Groups["name"].Value, names[4]);
            Assert.Equal("Version " + row.Groups["version"].Value, names[5]);
            Assert.Equal(row.Groups["copyright"].Value, names[0]);
            Assert.Equal(row.Groups["licence"].Value, names[14]);

            // And the licence the file names is the one whose text is bundled beside it for its family.
            Assert.Contains(names[14], OflAddresses);
            var family = names.GetValueOrDefault(16) ?? names[1];
            Assert.True(LicenceFiles.ContainsKey(family), $"{file} is of the family {family}, which has no licence text beside it.");
        }
    }

    [Fact]
    public void The_api_and_the_app_bundle_the_same_bytes_and_the_same_licence_texts()
    {
        var api = RepositoryRoot.File("Khadra.Infrastructure", "FinancialDocuments", "Fonts");
        var app = RepositoryRoot.File("Khadra.Mobile", "assets", "fonts");

        foreach (var font in Directory.GetFiles(api, "*.ttf"))
            Assert.Equal(File.ReadAllBytes(font), File.ReadAllBytes(Path.Combine(app, Path.GetFileName(font))));
        foreach (var licence in LicenceFiles.Values)
            Assert.Equal(File.ReadAllBytes(Path.Combine(api, licence)), File.ReadAllBytes(Path.Combine(app, licence)));
    }

    [Fact]
    public void The_licence_texts_ship_beside_the_assembly_that_embeds_the_fonts()
    {
        var shipped = Path.Combine(Path.GetDirectoryName(typeof(QuestPdfFinancialDocumentRenderer).Assembly.Location)!, "FinancialDocuments", "Fonts");

        foreach (var licence in LicenceFiles.Values.Append("FONTS.md"))
            Assert.True(File.Exists(Path.Combine(shipped, licence)), $"{licence} is not copied beside the assembly.");

        // And what the assembly embeds is exactly what the notice names.
        var embedded = typeof(QuestPdfFinancialDocumentRenderer).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("Khadra.FinancialDocuments.Fonts.", StringComparison.Ordinal))
            .Select(name => name["Khadra.FinancialDocuments.Fonts.".Length..])
            .Order(StringComparer.Ordinal);
        var named = NoticeRow().Matches(ReadNotice(Path.Combine(shipped, "FONTS.md"))).Select(match => match.Groups["file"].Value).Order(StringComparer.Ordinal);
        Assert.Equal(named, embedded);
    }

    /// <summary>
    /// A TrueType font's own strings, by name ID — 0 its copyright, 1 and 16 its family, 4 its full name, 5 its
    /// version, 14 its licence's address — as the Windows, US-English records carry them.
    /// </summary>
    private static Dictionary<int, string> NamesOf(byte[] font)
    {
        static int U16(byte[] bytes, int at) => BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(at, 2));

        var name = Enumerable.Range(0, U16(font, 4))
            .Select(index => 12 + (16 * index))
            .Single(record => Encoding.ASCII.GetString(font, record, 4) == "name");
        var table = checked((int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(name + 8, 4)));
        var strings = table + U16(font, table + 4);
        var names = new Dictionary<int, string>();
        for (var index = 0; index < U16(font, table + 2); index++)
        {
            var record = table + 6 + (12 * index);
            if (U16(font, record) == 3 && U16(font, record + 2) == 1 && U16(font, record + 4) == 0x409)
                names[U16(font, record + 6)] = Encoding.BigEndianUnicode.GetString(font, strings + U16(font, record + 10), U16(font, record + 8));
        }

        return names;
    }

    /// <summary>A notice's text with its line endings as LF, whichever the checkout wrote.</summary>
    /// <remarks>
    /// The row pattern anchors on `$`, which in a multiline .NET regex stops before `\n` and not before `\r`. A Windows
    /// checkout with `core.autocrlf=true` writes the notice in CRLF, every row then ended in `\r`, and the tests found no
    /// rows at all. The notice's content is what is under test, never the line endings it was checked out with.
    /// </remarks>
    private static string ReadNotice(string path) => File.ReadAllText(path).ReplaceLineEndings("\n");

    [GeneratedRegex(
        @"^\| `(?<file>[^`]+\.ttf)` \| (?<name>[^|]+?) \| (?<version>[^|]+?) \| (?<copyright>[^|]+?) \| (?<licence>[^|]+?) \| `(?<hash>[0-9a-f]{64})` \|$",
        RegexOptions.Multiline)]
    private static partial Regex NoticeRow();
}
