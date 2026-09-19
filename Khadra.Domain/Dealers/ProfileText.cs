using CSharpFunctionalExtensions;
using Khadra.Domain.Common;

namespace Khadra.Domain.Dealers;

/// <summary>
/// The rules a rental office's own words are held to before a customer reads them.
/// </summary>
/// <remarks>
/// One free text per section, in whatever language the office writes. The platform never translates
/// it and never rewrites it: the only changes are the ones that make two texts that LOOK identical
/// store identically.
/// </remarks>
public static class ProfileText
{
    /// <summary>The most a section may hold, counted after tidying.</summary>
    public const int MaxLength = 2000;

    /// <summary>
    /// The text tidied, or refused.
    /// </summary>
    /// <remarks>
    /// <para>Tidied: every line ending becomes <c>\n</c>, the ends are trimmed, and blank becomes null —
    /// a section with nothing in it is a section not written.</para>
    /// <para>Refused, never cut, when it is longer than <see cref="MaxLength"/>. A rental condition cut
    /// off mid-sentence can say the opposite of what the office wrote.</para>
    /// <para>Refused when it holds a control character other than a line break or a tab: nothing a
    /// person types produces one, and nothing a screen draws renders it honestly.</para>
    /// </remarks>
    public static Result<string?, Error> Normalize(string? value, PublicProfileSection section)
    {
        ArgumentNullException.ThrowIfNull(section);

        if (string.IsNullOrWhiteSpace(value))
            return (string?)null;

        var text = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

        if (!HasOnlyAllowedCharacters(text))
            return DealerErrors.InvalidProfileText(section);

        if (text.Length > MaxLength)
            return DealerErrors.ProfileTextTooLong(section);

        return text;
    }

    /// <summary>
    /// Whether every character is one <see cref="Normalize"/> keeps: anything but a control character,
    /// apart from line breaks and tabs.
    /// </summary>
    public static bool HasOnlyAllowedCharacters(string? value) =>
        value is null || !value.Any(character => char.IsControl(character) && character is not ('\n' or '\r' or '\t'));
}
