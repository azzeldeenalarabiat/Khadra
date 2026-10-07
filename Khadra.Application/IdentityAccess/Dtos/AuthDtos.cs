using Khadra.Domain.IdentityAccess;

namespace Khadra.Application.IdentityAccess.Dtos;

public sealed record UserDto(
    Guid Id,
    string Email,
    string FullName,
    string Phone,
    string Role,
    bool IsEmailVerified,
    bool MustChangePassword,
    DateTimeOffset CreatedAt)
{
    public static UserDto From(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new UserDto(
            user.Id,
            user.Email.Value,
            user.Name.Value,
            user.Phone.Value,
            user.Role.Name,
            user.IsEmailVerified,
            user.MustChangePassword,
            user.CreatedAt);
    }
}

public sealed record AuthTokensDto(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    UserDto User);

/// <param name="VerificationEmailSent">
/// Whether the mail server accepted the verification message. False does NOT mean the registration
/// failed — the account and its token are saved either way — it means nothing is on its way to that
/// inbox and the person should be told to ask for another link rather than sent off to wait.
/// </param>
/// <param name="ConsentsRecorded">
/// How many of the legal texts in force this registration recorded as accepted (Wave 4, W4-8); 0 when none was asked.
/// </param>
public sealed record RegisteredUserDto(Guid UserId, string Email, bool VerificationEmailSent, int ConsentsRecorded = 0);
