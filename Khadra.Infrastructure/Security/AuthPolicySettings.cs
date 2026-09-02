using Khadra.Application.Common.Ports;
using Khadra.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Security;

internal sealed class AuthPolicySettings(IOptions<AuthOptions> options) : IAuthPolicySettings
{
    private readonly AuthOptions _options = options.Value;

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(_options.RefreshTokenDays);

    public TimeSpan RefreshFamilyLifetime => TimeSpan.FromDays(_options.RefreshFamilyDays);

    public TimeSpan EmailVerificationLifetime => TimeSpan.FromHours(_options.EmailVerificationHours);

    public TimeSpan PasswordResetLifetime => TimeSpan.FromMinutes(_options.PasswordResetMinutes);

    public int PasswordMinimumLength => _options.PasswordMinimumLength;
}
