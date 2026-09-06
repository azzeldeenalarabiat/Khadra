using Khadra.Application.Common.Ports;
using Khadra.Domain.IdentityAccess;
using Microsoft.Extensions.Logging;

namespace Khadra.Application.IdentityAccess;

/// <summary>
/// Sends the auth emails, and REPORTS whether they went out.
///
/// A delivery problem still never fails the command — the account and its token are already
/// committed, so undoing a completed registration because a mail server hiccuped would throw away
/// something the person can still use. What changed is that the outcome is no longer swallowed.
///
/// This used to return void and log the exception, so every caller carried on as if the message had
/// gone. The console then told the person "we sent a verification link to you" whatever had actually
/// happened, and the one thing they needed to know — that nothing was coming and they should ask for
/// another — was in a server log they cannot read. A false confirmation is worse than an error: it
/// sends someone to wait at an empty inbox.
/// </summary>
public sealed partial class AuthEmailDispatcher(
    IAuthEmailComposer composer,
    IEmailSender sender,
    ILogger<AuthEmailDispatcher> logger)
{
    /// <returns><c>true</c> when the mail server accepted the message.</returns>
    public Task<bool> SendEmailVerificationAsync(User user, string rawToken, CancellationToken cancellationToken) =>
        TrySendAsync(user, () => composer.EmailVerification(user, rawToken), "email verification", cancellationToken);

    public Task<bool> SendPasswordResetAsync(User user, string rawToken, CancellationToken cancellationToken) =>
        TrySendAsync(user, () => composer.PasswordReset(user, rawToken), "password reset", cancellationToken);

    public Task<bool> SendEmployeeInvitationAsync(User user, string dealerName, string rawToken, CancellationToken cancellationToken) =>
        TrySendAsync(user, () => composer.EmployeeInvitation(user, dealerName, rawToken), "employee invitation", cancellationToken);

    public Task<bool> SendPasswordChangedAsync(User user, CancellationToken cancellationToken) =>
        TrySendAsync(user, () => composer.PasswordChanged(user), "password changed", cancellationToken);

    private async Task<bool> TrySendAsync(
        User user,
        Func<EmailMessage> compose,
        string kind,
        CancellationToken cancellationToken)
    {
        try
        {
            await sender.SendAsync(compose(), cancellationToken);
            return true;
        }
#pragma warning disable CA1031 // Delivery failures are reported to the caller, never thrown at it.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogDeliveryFailed(logger, kind, user.Id.ToString(), exception);
            return false;
        }
    }

    [LoggerMessage(1100, LogLevel.Error, "Failed to send the {Kind} email for user {UserId}")]
    private static partial void LogDeliveryFailed(ILogger logger, string kind, string userId, Exception exception);
}
