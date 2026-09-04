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

    public EmailMessage AdminInvitation(User user, string rawToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        // Same route the staff invitation uses: one screen redeems both, told apart by the token.
        var link = Link("accept-invitation", rawToken);
        var name = WebUtility.HtmlEncode(user.Name.Value);
        return new EmailMessage(
            user.Email.Value,
            user.Name.Value,
            "You have been invited to administer Khadra",
            $"<p>Hi {name},</p><p>You have been given an administrator account on Khadra. " +
            "Administrators review rental offices, decide disputes and can see every booking on the platform.</p>" +
            $"<p><a href=\"{link}\">Accept the invitation and choose your password</a></p>" +
            "<p>If you were not expecting this, ignore this message and tell whoever runs the platform; " +
            "nothing is set up until you accept.</p>",
            $"Hi {user.Name.Value},\n\nYou have been given an administrator account on Khadra. " +
            "Administrators review rental offices, decide disputes and can see every booking on the platform.\n\n" +
            $"Accept the invitation and choose your password:\n{link}\n\n" +
            "If you were not expecting this, ignore this message and tell whoever runs the platform.");
    }

    public EmailMessage EmployeeInvitation(User user, string dealerName, string rawToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var link = Link("accept-invitation", rawToken);
        var name = WebUtility.HtmlEncode(user.Name.Value);
        var business = WebUtility.HtmlEncode(dealerName);
        return new EmailMessage(
            user.Email.Value,
            user.Name.Value,
            $"{dealerName} has invited you to Khadra",
            $"<p>Hi {name},</p><p>{business} has added you as a member of staff on Khadra. " +
            "You will be able to see and answer the office's booking requests.</p>" +
            $"<p><a href=\"{link}\">Accept the invitation and choose your password</a></p>" +
            "<p>If you were not expecting this, you can ignore this message; nothing is set up until you accept.</p>",
            $"Hi {user.Name.Value},\n\n{dealerName} has added you as a member of staff on Khadra. " +
            "You will be able to see and answer the office's booking requests.\n\n" +
            $"Accept the invitation and choose your password:\n{link}\n\n" +
            "If you were not expecting this, ignore this message; nothing is set up until you accept.");
    }

    private string Link(string route, string rawToken) =>
        $"{_clientBaseUrl}/{route}?token={Uri.EscapeDataString(rawToken)}";
}
