using Khadra.Application.Common.Ports;
using Microsoft.Extensions.Logging;

namespace Khadra.Infrastructure.Notifications;

// Keeps the API bootable with no mail server: the message (including the link) goes to the log.
internal sealed partial class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        LogEmail(logger, message.ToAddress, message.Subject, message.TextBody);
        return Task.CompletedTask;
    }

    [LoggerMessage(1200, LogLevel.Information, "Email to {To} | {Subject}\n{Body}")]
    private static partial void LogEmail(ILogger logger, string to, string subject, string body);
}
