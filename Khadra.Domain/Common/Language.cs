namespace Khadra.Domain.Common;

/// <summary>
/// The two languages this platform is written in.
/// </summary>
/// <remarks>
/// <para>
/// Not a general list of world languages and not an open set: Khadra serves Jordan in Arabic and
/// English, both clients ship exactly these two, and a third would be a product decision with a
/// translation budget behind it rather than a new row.
/// </para>
/// <para>
/// The names are the ISO 639-1 codes because they travel: they are what `Accept-Language` carries,
/// what the ARB files and the console dictionaries are keyed by, and what the column suffixes are
/// named after. One spelling everywhere is what stops `ar-JO`, `Arabic` and `AR` all appearing.
/// </para>
/// </remarks>
public sealed class Language : Enumeration
{
    public static readonly Language Arabic = new(1, "ar");
    public static readonly Language English = new(2, "en");

    private Language(int id, string name) : base(id, name)
    {
    }

    /// <summary>
    /// What a customer gets when nobody said.
    ///
    /// English, because it is the language the platform's own fallbacks and every log line are in —
    /// NOT because it is the more likely one. A customer app that means Arabic says so on every
    /// request; a caller that says nothing is a script, a probe or an old build, and the honest thing
    /// is to answer them in the language the rest of the system already speaks.
    /// </summary>
    public static Language Default => English;

    /// <summary>
    /// The language an <c>Accept-Language</c> value asks for, or <see cref="Default"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately forgiving and deliberately small. It reads the first tag, ignores the region
    /// (`ar-JO` is Arabic) and ignores q-weights, because this is a two-language platform: there is
    /// no negotiation to do, only a question of which of two. Anything unrecognised is the default
    /// rather than an error — a header a customer never sees must not be able to fail their request.
    /// </remarks>
    public static Language FromHeader(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Default;

        foreach (var entry in value.Split(','))
        {
            // "ar-JO;q=0.9" -> "ar-JO" -> "ar"
            var tag = entry.Split(';')[0].Trim();
            var primary = tag.Split('-')[0];

            foreach (var language in GetAll<Language>())
            {
                if (string.Equals(primary, language.Name, StringComparison.OrdinalIgnoreCase))
                    return language;
            }
        }

        return Default;
    }

    /// <summary>The other one. Two languages, so a fallback has exactly one place to go.</summary>
    /// <remarks>
    /// A METHOD, not a property, and that is not a style preference. As a property it made every
    /// object holding a <see cref="Language"/> non-serialisable: `System.Text.Json` walks properties,
    /// `ar.Other` is `en`, `en.Other` is `ar`, and the writer recursed until it threw "a possible
    /// object cycle was detected" — thirty-two levels deep, as a 500, on the public gallery page.
    ///
    /// The right fix for that one endpoint was a DTO, and it has one. This is the second fix: a
    /// smart enum that points at another instance of itself from a property is a trap for whoever
    /// next puts one on the wire, and a method is not walked.
    /// </remarks>
    public Language Other() => this == Arabic ? English : Arabic;
}
