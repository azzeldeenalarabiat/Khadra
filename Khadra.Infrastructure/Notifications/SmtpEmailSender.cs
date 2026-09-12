using Khadra.Application.Common.Ports;
using Khadra.Infrastructure.Configuration;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Khadra.Infrastructure.Notifications;

internal sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Falls back to the authenticated account, because that is what Gmail and most relays
        // require the From to be; a mismatch is rejected or silently rewritten. Failing here with a
        // sentence an operator can act on beats a MailKit protocol error three frames down.
        var from = string.IsNullOrWhiteSpace(_options.FromAddress) ? _options.Username : _options.FromAddress;
        if (string.IsNullOrWhiteSpace(from))
        {
            throw new InvalidOperationException(
                "Email:FromAddress is empty and no Email:Username is configured, so there is no address " +
                "to send from. Set Email:Username (and Email:Password) in user-secrets.");
        }

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_options.FromName, from));
        mime.To.Add(new MailboxAddress(message.ToName, message.ToAddress));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody }.ToMessageBody();

        // The attempts SHARE the timeout budget, so more attempts never mean a longer wait for the
        // person watching the form. See EmailOptions.MaxAttempts for why one attempt is not enough.
        var attempts = Math.Max(1, _options.MaxAttempts);
        var perAttemptMs = Math.Max(1000, _options.TimeoutSeconds * 1000 / attempts);

        // The budget is enforced HERE, by a clock, because SmtpClient.Timeout alone does not enforce
        // it. That property bounds one socket operation, and SendOnceAsync performs four of them --
        // connect, authenticate, send, disconnect -- so a single attempt could take four times its
        // slice and three attempts twelve times it. TimeoutSeconds is documented as the TOTAL wait a
        // caller may suffer; against a mail server that answered slowly it was really a floor, and a
        // registration ran past the phone's own receive timeout because of it.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await SendOnceAsync(mime, perAttemptMs, budget.Token);
                return;
            }
            catch (Exception exception) when (attempt < attempts && IsTransient(exception)
                                             && !budget.IsCancellationRequested)
            {
                // Deliberately no logging here: the dispatcher reports the final outcome, and a line
                // per retry would make a recovered send look like a failure to anyone reading the log.
                //
                // A spent budget stops the retries even when the failure looks transient: another go
                // cannot finish inside a window that has already closed.
                await Task.Delay(TimeSpan.FromMilliseconds(250), budget.Token);
            }
        }
    }

    private async Task SendOnceAsync(MimeMessage mime, int timeoutMs, CancellationToken cancellationToken)
    {
        using var client = new SmtpClient { Timeout = timeoutMs };
        var socketOptions = _options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
        await client.ConnectAsync(_options.Host, _options.Port, socketOptions, cancellationToken);
        if (!string.IsNullOrEmpty(_options.Username))
            await client.AuthenticateAsync(_options.Username, _options.Password ?? string.Empty, cancellationToken);
        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }

    /// <summary>
    /// Whether trying the same message again could plausibly get a different answer.
    /// </summary>
    /// <remarks>
    /// Credentials the server refused, and a sender or recipient it rejected outright, are settled:
    /// retrying them only spends the caller's budget to be told the same thing three times. Anything
    /// else -- a stalled greeting, a dropped socket, a 4xx "try later" -- is worth another go.
    /// </remarks>
    private static bool IsTransient(Exception exception) => exception switch
    {
        AuthenticationException => false,
        SmtpCommandException { StatusCode: >= (SmtpStatusCode)500 } => false,
        OperationCanceledException => false,
        _ => true,
    };
}
