using Khadra.Domain.IdentityAccess;

namespace Khadra.Application.Common.Ports;

public sealed record EmailMessage(string ToAddress, string ToName, string Subject, string HtmlBody, string TextBody);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

// Builds the auth emails; the raw one-time token is embedded in a link to the client application.
public interface IAuthEmailComposer
{
    EmailMessage EmailVerification(User user, string rawToken);

    EmailMessage PasswordReset(User user, string rawToken);

    EmailMessage PasswordChanged(User user);
}
