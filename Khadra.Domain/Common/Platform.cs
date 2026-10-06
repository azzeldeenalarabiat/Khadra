using System.Globalization;
using System.Text;

namespace Khadra.Domain.Common;

/// <summary>The platform's own identity, named once for every context that needs it.</summary>
public static class Platform
{
    /// <summary>
    /// "Khadra": the actor name of everything the platform itself does (a refund it sends, a decision it makes), and
    /// a name no rental office may register under.
    /// </summary>
    public const string Name = "Khadra";

    /// <summary>
    /// The names no rental office may take, because each reads as the platform itself (owner, 2026-10-05 and
    /// 2026-10-06; pre-launch item 230): the brand in Latin script, and in Arabic both as the brand is written and in
    /// the dictionary spelling with hamza.
    /// </summary>
    /// <remarks>
    /// The reason is concrete, not cosmetic. A notification with no actor user whose actor name is exactly
    /// <see cref="Name"/> is worded as the platform's own (<c>Notification.IsFromPlatform</c>), and an office's own
    /// notifications to its customers carry its business name the same way, so an office approved as "Khadra" would
    /// have its refunds and bookings announced as the platform's.
    /// </remarks>
    public static IReadOnlyList<string> ReservedNames { get; } = [Name, "خضرا", "خضراء"];

    /// <summary>
    /// Whether <paramref name="name"/> is one of <see cref="ReservedNames"/>, as a reader would see it.
    /// </summary>
    /// <remarks>
    /// Exact names only — "Khadra Rentals" is a different business — but compared the way they look rather than the
    /// way they are encoded: ignoring letter case, Unicode compatibility forms (a full-width "Ｋｈａｄｒａ", Arabic
    /// presentation forms), invisible format characters (zero-width spaces and joiners, direction marks), Arabic
    /// diacritics and tatweel, and runs of spacing. Each of those would otherwise put the platform's name on screen
    /// under a spelling a byte comparison calls different.
    /// </remarks>
    public static bool IsReservedName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        var candidate = Comparable(name);
        return ReservedNames.Any(reserved => string.Equals(Comparable(reserved), candidate, StringComparison.Ordinal));
    }

    private static string Comparable(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormKC);
        var kept = new StringBuilder(normalized.Length);
        var lastWasSpace = false;
        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            // Invisible: zero-width spaces and joiners, direction marks, the byte-order mark.
            if (category == UnicodeCategory.Format)
                continue;
            // Arabic diacritics (harakat, shadda, sukun, superscript alef) and the tatweel that stretches a word.
            if (category == UnicodeCategory.NonSpacingMark || character == 'ـ')
                continue;
            if (char.IsWhiteSpace(character))
            {
                if (!lastWasSpace && kept.Length > 0)
                    kept.Append(' ');
                lastWasSpace = true;
                continue;
            }
            kept.Append(char.ToUpperInvariant(character));
            lastWasSpace = false;
        }
        return kept.ToString().TrimEnd();
    }
}
