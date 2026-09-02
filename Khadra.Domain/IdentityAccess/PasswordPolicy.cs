using CSharpFunctionalExtensions;
using Khadra.Domain.Common;

namespace Khadra.Domain.IdentityAccess;

// Pure domain policy. The minimum length is configurable; the maximum is bcrypt's 72-byte input cap.
public static class PasswordPolicy
{
    public const int AbsoluteMinimumLength = 8;
    public const int MaximumLength = 72;

    public static UnitResult<Error> Validate(string? password, int minimumLength)
    {
        var effectiveMinimum = Math.Max(minimumLength, AbsoluteMinimumLength);

        if (string.IsNullOrEmpty(password))
            return UnitResult.Failure(IdentityErrors.WeakPassword("A password is required."));
        if (password.Length < effectiveMinimum)
            return UnitResult.Failure(IdentityErrors.WeakPassword($"The password must be at least {effectiveMinimum} characters."));
        if (password.Length > MaximumLength)
            return UnitResult.Failure(IdentityErrors.WeakPassword($"The password must be at most {MaximumLength} characters."));
        if (!password.Any(char.IsLetter))
            return UnitResult.Failure(IdentityErrors.WeakPassword("The password must contain at least one letter."));
        if (!password.Any(char.IsDigit))
            return UnitResult.Failure(IdentityErrors.WeakPassword("The password must contain at least one digit."));
        if (password.Any(char.IsWhiteSpace))
            return UnitResult.Failure(IdentityErrors.WeakPassword("The password must not contain spaces."));

        return UnitResult.Success<Error>();
    }
}
