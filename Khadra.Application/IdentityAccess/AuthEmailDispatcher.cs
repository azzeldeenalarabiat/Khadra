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
///
/// Callers pick one of THREE shapes, and picking at random is how false confirmations get back in:
///
/// <list type="bullet">
/// <item>The commit created something the caller must know about (an account, an employee): return
/// success WITH an <c>…EmailSent</c> flag. Failing would discard a real record over a mail hiccup,
/// and the caller still needs to be told nobody was written to.</item>
/// <item>The commit created only a replaceable token (a reset link, a re-sent invitation): return a
/// FAILURE. Trying again is harmless — it reissues — and is exactly what the message asks for.</item>
/// <item>A notice with nothing for the caller to do differently ("your password changed"): discard
/// the result. It is logged at Error and that is the whole remedy.</item>
/// </list>
///
/// And: nothing calls <see cref="IEmailSender"/> directly after a commit. An unhandled send is how
/// the bootstrapper used to kill the process on first boot with a misconfigured relay.
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

    public Task<bool> SendAdminInvitationAsync(User user, string rawToken, CancellationToken cancellationToken) =>
        TrySendAsync(user, () => composer.AdminInvitation(user, rawToken), "admin invitation", cancellationToken);

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
