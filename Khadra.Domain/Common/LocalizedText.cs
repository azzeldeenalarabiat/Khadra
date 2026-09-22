namespace Khadra.Domain.Common;

/// <summary>One language's worth of text, and which language it turned out to be.</summary>
/// <remarks>
/// The language travels WITH the text because the answer may not be the language that was asked
/// for — see <see cref="LocalizedText.Resolve"/>. It is the office's CLAIM about what it typed, not
/// a property of the characters: direction still comes from the text itself on both clients, because
/// an office that types Arabic into the English box must still get a readable paragraph. What the
/// tag is for is `lang` (the console zeroes letter-spacing under `:lang(ar)`) and for saying "shown
/// in English" honestly, should anyone want to.
/// </remarks>
public readonly record struct ResolvedText(string Text, Language Language);

/// <summary>
/// A piece of text a rental office wrote, in Arabic, in English, or in one of the two.
/// </summary>
/// <remarks>
/// <para>
/// **Both are optional and that is the point.** An office writes what it has time to write. The
/// platform's job is to show a customer whatever the office DID write rather than an empty heading,
/// so a missing language falls back to the one that exists.
/// </para>
/// <para>
/// **Nothing here translates anything.** There is no machine translation on this platform and no
/// language is ever written into the other's column. When an office has written only Arabic, an
/// English customer is shown that Arabic — the office's own words, which is the only thing the
/// platform can honestly show — and the answer says it is Arabic.
/// </para>
/// <para>
/// **This is a DOMAIN shape, never a storage one.** It is computed from two ordinary `string?`
/// columns and is not mapped. Mapping it as an owned type would be the trap `DealerConfiguration`
/// documents twice: an owned type whose columns are all nullable cannot be told apart from an absent
/// one, so EF materialises it as null — and a section nobody has written yet is exactly that case,
/// which is to say most of them. A struct also cannot be handed to two mapped properties by
/// accident, which is the other hazard that file warns about.
/// </para>
/// <para>
/// **The text is normalised by whoever owns it**, not here: a profile section and a vehicle
/// description have their own rules and their own error codes. This holds two already-clean strings
/// and answers one question about them, which is what lets `Fleet` use it without knowing what a
/// `PublicProfileSection` is.
/// </para>
/// </remarks>
public readonly record struct LocalizedText(string? Ar, string? En)
{
    /// <summary>Nothing written in either language.</summary>
    public bool IsEmpty => Ar is null && En is null;

    /// <summary>Blank in either language is "not written", so a cleared box and an untouched one
    /// are the same thing to every reader.</summary>
    public static LocalizedText From(string? ar, string? en) =>
        new(Blank(ar) ? null : ar, Blank(en) ? null : en);

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);

    /// <summary>The text in one language, or null if that language was not written.</summary>
    public string? In(Language language)
    {
        ArgumentNullException.ThrowIfNull(language);
        return language == Language.Arabic ? Ar : En;
    }

    /// <summary>
    /// What to show a reader of <paramref name="language"/>, or null if the office wrote nothing at
    /// all.
    /// </summary>
    /// <remarks>
    /// The whole fallback rule, in one place, on the server — which is the point of it living here
    /// rather than on two clients that cannot see each other or agree. Asked for Arabic: Arabic if
    /// there is any, otherwise English. Asked for English: the mirror. Nothing written: null, and
    /// the section does not appear.
    ///
    /// Null is "nothing to show" and says nothing about why — the same contract the gallery sections
    /// already keep, where hidden, never-written and not-applicable all arrive as null and no screen
    /// may ask which.
    /// </remarks>
    public ResolvedText? Resolve(Language language)
    {
        ArgumentNullException.ThrowIfNull(language);

        var preferred = In(language);
        if (preferred is not null)
            return new ResolvedText(preferred, language);

        var other = In(language.Other());
        return other is null ? null : new ResolvedText(other, language.Other());
    }
}

/// <summary>
/// One field as a caller submitted it: the Arabic box and the English box, untouched.
/// </summary>
/// <remarks>
/// A record rather than two loose strings because these travel in pairs through seven call sites,
/// and a transposed pair — Arabic saved as English — is the one mistake here that nothing downstream
/// could detect and no test would fail on.
/// </remarks>
/// <remarks>
/// A STRUCT, so `default` means "nothing written" rather than a null reference. Seven of these
/// travel together through every write path, and one forgotten null check would have been an
/// exception on a form somebody was filling in.
/// </remarks>
public readonly record struct LocalizedInput(string? Ar, string? En)
{
    public static LocalizedInput Nothing => default;
}
