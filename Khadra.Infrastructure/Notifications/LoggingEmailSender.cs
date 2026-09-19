using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace Khadra.Infrastructure.Notifications;

// Keeps the API bootable with no mail server. It delivers NOTHING, and its receipt says so.
//
// It used to write the whole text body to the log. The body of every auth email is a link carrying a
// one-time token, so the log held working verification links, password resets and invitations for
// anybody who could read it. It now writes the subject and the recipient's domain: enough to see that
// a message would have gone, and nothing anybody could use. To follow a link locally, use Mailpit or a
// real transport.
internal sealed partial class LoggingEmailSender(IClock clock, ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task<EmailSendReceipt> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        LogNotDelivered(logger, message.Subject, message.RecipientDomain);
        return Task.FromResult(new EmailSendReceipt(
            EmailOptions.LoggingProvider,
            ProviderMessageId: null,
            clock.UtcNow,
            Attempts: 1,
            ProviderResponse: "not delivered: logging transport"));
    }

    // 1400, its own range: 1200 is the booking dispatcher's, and a search for one event should not
    // return two unrelated things.
    [LoggerMessage(1400, LogLevel.Information,
        "Email NOT delivered, because Email:Provider is Logging: \"{Subject}\" for an address at {RecipientDomain}")]
    private static partial void LogNotDelivered(ILogger logger, string subject, string recipientDomain);
}
