using System.Globalization;
using System.Text.RegularExpressions;

namespace Khadra.Application.Common;

/// <summary>
/// The version a customer app reports for itself, and the ONE rule for ordering two of them.
/// </summary>
/// <remarks>
/// <para>
/// **Semantic Versioning 2.0.0, exactly.** <c>MAJOR.MINOR.PATCH</c>, each a number with no leading
/// zero; an optional <c>-prerelease</c> of dot-separated identifiers; an optional <c>+build</c>, which
/// is ignored — the store build number rides there (<c>1.1.0+2</c>) and says nothing about which
/// contract the app speaks. Nothing else parses: not <c>v1.1.0</c>, not <c>1.1</c>, not a
/// surrounding space.
/// </para>
/// <para>
/// **Never compared as text.** As strings <c>"1.10.0" &lt; "1.9.0"</c>, which would lock out every
/// build from 1.10 onwards the day the minimum reached 1.9. Precedence is numeric, field by field; a
/// prerelease ranks BELOW the release it leads up to (so <c>1.1.0-rc.1</c> does not satisfy a minimum
/// of <c>1.1.0</c>); between prereleases, numeric identifiers compare as numbers, others by ordinal
/// text, numbers rank below words, and more identifiers rank above fewer. Deterministic, and total.
/// </para>
/// <para>
/// The customer app carries a twin of this type (<c>lib/core/config/app_version.dart</c>). Both are
/// tested against the same vectors, because the gate only works if the server and the phone agree
/// about which of two versions is newer.
/// </para>
/// </remarks>
public readonly partial record struct AppVersion : IComparable<AppVersion>
{
    private AppVersion(int major, int minor, int patch, string? prerelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
    }

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    /// <summary>The prerelease identifiers as written, or null for a release.</summary>
    public string? Prerelease { get; }

    public bool IsPrerelease => Prerelease is not null;

    public static bool TryParse(string? text, out AppVersion version)
    {
        version = default;
        if (text is null)
            return false;

        var match = Grammar().Match(text);
        if (!match.Success)
            return false;

        if (!TryNumber(match.Groups["major"].Value, out var major)
            || !TryNumber(match.Groups["minor"].Value, out var minor)
            || !TryNumber(match.Groups["patch"].Value, out var patch))
            return false;

        var prerelease = match.Groups["pre"].Success ? match.Groups["pre"].Value : null;
        version = new AppVersion(major, minor, patch, prerelease);
        return true;
    }

    public int CompareTo(AppVersion other)
    {
        var core = Major.CompareTo(other.Major);
        if (core != 0) return core;
        core = Minor.CompareTo(other.Minor);
        if (core != 0) return core;
        core = Patch.CompareTo(other.Patch);
        if (core != 0) return core;

        // A release outranks any prerelease of itself.
        if (Prerelease is null) return other.Prerelease is null ? 0 : 1;
        if (other.Prerelease is null) return -1;

        var mine = Prerelease.Split('.');
        var theirs = other.Prerelease.Split('.');
        for (var index = 0; index < Math.Min(mine.Length, theirs.Length); index++)
        {
            var order = CompareIdentifier(mine[index], theirs[index]);
            if (order != 0) return order;
        }

        return mine.Length.CompareTo(theirs.Length);
    }

    public static bool operator <(AppVersion left, AppVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(AppVersion left, AppVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(AppVersion left, AppVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(AppVersion left, AppVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() =>
        Prerelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{Prerelease}";

    private static int CompareIdentifier(string left, string right)
    {
        var leftIsNumber = IsNumeric(left);
        var rightIsNumber = IsNumeric(right);
        return (leftIsNumber, rightIsNumber) switch
        {
            // Lengths first: neither has a leading zero, so the longer number is the larger one, and
            // no identifier is ever too long to compare.
            (true, true) => left.Length != right.Length
                ? left.Length.CompareTo(right.Length)
                : string.CompareOrdinal(left, right),
            (true, false) => -1,
            (false, true) => 1,
            _ => Math.Sign(string.CompareOrdinal(left, right)),
        };
    }

    private static bool IsNumeric(string identifier) => identifier.All(char.IsAsciiDigit);

    // Nine digits keeps every field inside an int. Nobody versions an app past 999,999,999, and a
    // header that tries is refused rather than overflowing into a small number.
    private static bool TryNumber(string digits, out int value) =>
        int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    // The grammar from semver.org, with each core field capped at nine digits. `[0-9]` throughout,
    // never `\d`, which in .NET also matches Arabic-Indic and every other Unicode digit.
    [GeneratedRegex(
        @"^(?<major>0|[1-9][0-9]{0,8})\.(?<minor>0|[1-9][0-9]{0,8})\.(?<patch>0|[1-9][0-9]{0,8})"
        + @"(?:-(?<pre>(?:0|[1-9][0-9]*|[0-9]*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[a-zA-Z-][0-9a-zA-Z-]*))*))?"
        // `\z`, not `$`: in .NET `$` also matches before a trailing newline, so "1.1.0\n" would pass.
        + @"(?:\+[0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*)?\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex Grammar();
}
