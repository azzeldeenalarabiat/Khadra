using Khadra.Application.Common.Ports;
using Khadra.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Security;

internal sealed class AuthPolicySettings(IOptions<AuthOptions> options) : IAuthPolicySettings
{
    private readonly AuthOptions _options = options.Value;

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(_options.RefreshTokenDays);

    public TimeSpan RefreshFamilyLifetime => TimeSpan.FromDays(_options.RefreshFamilyDays);

    public TimeSpan RefreshReuseGrace => TimeSpan.FromSeconds(_options.RefreshReuseGraceSeconds);

    public TimeSpan EmailVerificationLifetime => TimeSpan.FromHours(_options.EmailVerificationHours);

    public TimeSpan PasswordResetLifetime => TimeSpan.FromMinutes(_options.PasswordResetMinutes);

    public TimeSpan EmployeeInvitationLifetime => TimeSpan.FromDays(_options.EmployeeInvitationDays);

    public int PasswordMinimumLength => _options.PasswordMinimumLength;
}
