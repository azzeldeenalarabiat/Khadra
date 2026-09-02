using System.Net;
using Khadra.Application.Common.Ports;
using Khadra.Domain.IdentityAccess;
using Khadra.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Notifications;

// English templates for now; Arabic/bilingual templates are a follow-up (plan §13).
internal sealed class AuthEmailComposer(IOptions<AppOptions> options) : IAuthEmailComposer
{
    private readonly string _clientBaseUrl = options.Value.ClientBaseUrl.TrimEnd('/');

    public EmailMessage EmailVerification(User user, string rawToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var link = Link("verify-email", rawToken);
        var name = WebUtility.HtmlEncode(user.Name.Value);
        return new EmailMessage(
            user.Email.Value,
            user.Name.Value,
            "Verify your Khadra email address",
            $"<p>Hi {name},</p><p>Confirm your email address to start using Khadra:</p>" +
            $"<p><a href=\"{link}\">Verify my email</a></p><p>If you did not create an account, ignore this message.</p>",
            $"Hi {user.Name.Value},\n\nConfirm your email address to start using Khadra:\n{link}\n\nIf you did not create an account, ignore this message.");
    }

    public EmailMessage PasswordReset(User user, string rawToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var link = Link("reset-password", rawToken);
        var name = WebUtility.HtmlEncode(user.Name.Value);
        return new EmailMessage(
            user.Email.Value,
            user.Name.Value,
            "Reset your Khadra password",
            $"<p>Hi {name},</p><p>We received a request to reset your password:</p>" +
            $"<p><a href=\"{link}\">Choose a new password</a></p><p>If you did not request this, you can ignore this message; your password stays unchanged.</p>",
            $"Hi {user.Name.Value},\n\nWe received a request to reset your password:\n{link}\n\nIf you did not request this, ignore this message; your password stays unchanged.");
    }

    public EmailMessage PasswordChanged(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var name = WebUtility.HtmlEncode(user.Name.Value);
        return new EmailMessage(
            user.Email.Value,
            user.Name.Value,
            "Your Khadra password was changed",
            $"<p>Hi {name},</p><p>Your password was just changed and all other sessions were signed out. " +
            "If this was not you, reset your password immediately and contact support.</p>",
            $"Hi {user.Name.Value},\n\nYour password was just changed and all other sessions were signed out. " +
            "If this was not you, reset your password immediately and contact support.");
    }

    private string Link(string route, string rawToken) =>
        $"{_clientBaseUrl}/{route}?token={Uri.EscapeDataString(rawToken)}";
}
