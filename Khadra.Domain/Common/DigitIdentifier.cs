namespace Khadra.Domain.Common;

/// <summary>
/// Normalises an identifier that is written as digits but read out with separators — a commercial
/// registration number, a plate.
/// </summary>
/// <remarks>
/// Both of those used to be built with <c>raw.Where(char.IsAsciiDigit)</c>, which silently DELETED
/// everything else instead of refusing it. Typing "E2E20260906" stored "220260906": a different
/// number, accepted without a word, and shown back as the licence of record that an administrator
/// then checks against the uploaded certificate. Worse, both columns are UNIQUE, so "AB-1234" and
/// "1234" normalise to the same value and the second applicant is refused for a number they never
/// entered.
///
/// The intent was only ever to let people type the separators they see on the document. So that is
/// all this removes; anything else is the caller's mistake and is returned as one.
/// </remarks>
public static class DigitIdentifier
{
    /// <summary>Characters people put in these numbers for legibility, and nothing more.</summary>
    private const string Separators = " -/.";

    /// <summary>
    /// Digits only, with separators dropped, or <c>null</c> when the input holds anything else.
    /// </summary>
    public static string? Normalise(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var digits = new char[raw.Length];
        var length = 0;
        foreach (var character in raw)
        {
            if (char.IsAsciiDigit(character))
                digits[length++] = character;
            else if (!Separators.Contains(character, StringComparison.Ordinal))
                return null;
        }

        return length == 0 ? null : new string(digits, 0, length);
    }
}
