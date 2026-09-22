using Khadra.Domain.Common;

namespace Khadra.Application.Common.Dtos;

/// <summary>
/// A piece of an office's own writing, and which language it turned out to be in.
/// </summary>
/// <remarks>
/// <para>
/// **Why the language travels with it.** The office may not have written the language the customer
/// asked for, and the platform shows what the office DID write rather than an empty heading. So the
/// text in this field is sometimes the other language, and a screen that assumed otherwise would put
/// English words under an Arabic heading with nothing to say so.
/// </para>
/// <para>
/// **What it is NOT for: direction.** Both clients already lay out office-typed text from the text
/// itself — `UserText` in the app, `.user-text` in the console — and that has to stay, because an
/// office that types Arabic into the English box must still get a readable paragraph. This is the
/// office's CLAIM about what it wrote, which is the right thing to hang `lang` on and to word an
/// honest "shown in English" note from; it is not a measurement of the characters.
/// </para>
/// <para>
/// Null, where one of these appears, means "nothing to show" and never says why — hidden, never
/// written and not-applicable all arrive the same way, which is the contract the gallery sections
/// have always kept.
/// </para>
/// </remarks>
public sealed record ResolvedTextDto(string Text, string Language)
{
    public static ResolvedTextDto? From(ResolvedText? resolved) =>
        resolved is { } value ? new ResolvedTextDto(value.Text, value.Language.Name) : null;
}

/// <summary>
/// One field in both languages, exactly as the office typed them.
/// </summary>
/// <remarks>
/// What an OWNER and an ADMIN see, and deliberately never resolved: an office that has written only
/// Arabic must find an empty English box, because that empty box is the work still to do. Falling
/// back here would show them their own Arabic under the English label and quietly report the page as
/// finished.
///
/// It is also what the console saves back, so what is read and what is written have one shape.
/// </remarks>
public sealed record LocalizedTextDto(string? Ar, string? En)
{
    public static LocalizedTextDto From(LocalizedText text) => new(text.Ar, text.En);

    public LocalizedInput ToInput() => new(Ar, En);
}
