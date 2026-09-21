using System.Globalization;
using System.Security.Claims;
using System.Text;
using Khadra.Application.Common.Ports;
using Khadra.Domain.IdentityAccess;
using Khadra.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Khadra.Infrastructure.Security;

public static class KhadraClaimTypes
{
    public const string Subject = "sub";
    public const string Email = "email";
    public const string Name = "name";
    public const string Role = "role";
    public const string EmailVerified = "email_verified";
    public const string SecurityStamp = "khadra:security_stamp";
    public const string MustChangePassword = "khadra:must_change_password";

    /// <summary>The refresh-token family this access token was minted for: one sign-in, one device.</summary>
    /// <remarks>
    /// The registered <c>sid</c> name rather than a <c>khadra:</c> one, because that is what it is.
    /// Tokens minted before this claim existed simply do not carry it, and nothing may refuse a
    /// request for that: it is worth at most fifteen minutes of "this device" not being marked.
    /// </remarks>
    public const string SessionId = "sid";
}

internal sealed class JwtAccessTokenIssuer(IOptions<JwtOptions> options) : IAccessTokenIssuer
{
    private readonly JwtOptions _options = options.Value;
    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

    public IssuedAccessToken Issue(User user, Guid sessionId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(user);

        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);
        var claims = new Dictionary<string, object>
        {
            [KhadraClaimTypes.Subject] = user.Id.ToString(),
            [KhadraClaimTypes.Email] = user.Email.Value,
            [KhadraClaimTypes.Name] = user.Name.Value,
            [KhadraClaimTypes.Role] = user.Role.Name,
            [KhadraClaimTypes.EmailVerified] = user.IsEmailVerified,
            [KhadraClaimTypes.SecurityStamp] = user.SecurityStamp.ToString("N"),
            [KhadraClaimTypes.MustChangePassword] = user.MustChangePassword,
            [KhadraClaimTypes.SessionId] = sessionId.ToString("N"),
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N")
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
                SecurityAlgorithms.HmacSha256)
        };

        return new IssuedAccessToken(_handler.CreateToken(descriptor), expiresAt);
    }
}
