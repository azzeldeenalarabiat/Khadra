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

    /// <summary>Spec 4.2: a dealer owner invited this person to act for the business.</summary>
    EmailMessage EmployeeInvitation(User user, string dealerName, string rawToken);

    /// <summary>An administrator invited by another. Same accept route, different words.</summary>
    EmailMessage AdminInvitation(User user, string rawToken);
}
