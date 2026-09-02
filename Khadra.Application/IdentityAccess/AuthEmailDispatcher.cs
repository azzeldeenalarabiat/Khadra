using Khadra.Application.Common.Ports;
using Khadra.Domain.IdentityAccess;
using Microsoft.Extensions.Logging;

namespace Khadra.Application.IdentityAccess;

// Sends auth emails without letting delivery problems fail the command: the token is already
// persisted, so the user can request a new link. Failures are logged for operators.
public sealed partial class AuthEmailDispatcher(
    IAuthEmailComposer composer,
    IEmailSender sender,
    ILogger<AuthEmailDispatcher> logger)
{
    public Task SendEmailVerificationAsync(User user, string rawToken, CancellationToken cancellationToken) =>
        TrySendAsync(user, () => composer.EmailVerification(user, rawToken), "email verification", cancellationToken);

    public Task SendPasswordResetAsync(User user, string rawToken, CancellationToken cancellationToken) =>
        TrySendAsync(user, () => composer.PasswordReset(user, rawToken), "password reset", cancellationToken);

    public Task SendPasswordChangedAsync(User user, CancellationToken cancellationToken) =>
        TrySendAsync(user, () => composer.PasswordChanged(user), "password changed", cancellationToken);

    private async Task TrySendAsync(User user, Func<EmailMessage> compose, string kind, CancellationToken cancellationToken)
    {
        try
        {
            await sender.SendAsync(compose(), cancellationToken);
        }
#pragma warning disable CA1031 // Delivery failures must never surface as command failures.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogDeliveryFailed(logger, kind, user.Id.ToString(), exception);
        }
    }

    [LoggerMessage(1100, LogLevel.Error, "Failed to send the {Kind} email for user {UserId}")]
    private static partial void LogDeliveryFailed(ILogger logger, string kind, string userId, Exception exception);
}
