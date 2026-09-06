using Khadra.Application.Common.Ports;
using Khadra.Infrastructure.Configuration;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Notifications;

/// <summary>
/// Opens a real SMTP session, authenticates, and hangs up without sending anything.
///
/// The three answers it separates are the three different problems: the host did not answer (network,
/// port, DNS), it answered but refused the credentials (wrong or missing password), or it accepted
/// them (mail will go out). Collapsing those into "email is broken" is what makes this class of
/// misconfiguration take an afternoon.
/// </summary>
internal sealed class SmtpTransportProbe(IOptions<EmailOptions> options) : IEmailTransportProbe
{
    private readonly EmailOptions _options = options.Value;

    public async Task<EmailTransportStatus> CheckAsync(CancellationToken cancellationToken = default)
    {
        var sender = string.IsNullOrWhiteSpace(_options.FromAddress) ? _options.Username : _options.FromAddress;
        if (string.IsNullOrWhiteSpace(sender))
        {
            return new EmailTransportStatus(false, false, false,
                "No sender is configured. Set Email:Username (and Email:Password) in user-secrets or the environment.");
        }

        if (string.IsNullOrEmpty(_options.Username) || string.IsNullOrEmpty(_options.Password))
        {
            // Not an error, and deliberately not reported as one: this is what a half-finished setup
            // looks like, and saying so plainly is more use than a stack trace from the mail library.
            return new EmailTransportStatus(true, false, false,
                $"No Email:Username/Password is set, so {_options.Host} will refuse every message. " +
                "Gmail needs a Google App Password (myaccount.google.com/apppasswords), not the account password.");
        }

        // Startup must not stall behind a relay that never answers; the verdict is what matters.
        using var client = new SmtpClient { Timeout = _options.TimeoutSeconds * 1000 };
        var socketOptions = _options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
        try
        {
            await client.ConnectAsync(_options.Host, _options.Port, socketOptions, cancellationToken);
        }
#pragma warning disable CA1031 // Every failure here is reported, never thrown at startup.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            return new EmailTransportStatus(true, false, false,
                $"Could not reach {_options.Host}:{_options.Port} — {exception.Message}");
        }

        try
        {
            await client.AuthenticateAsync(_options.Username, _options.Password, cancellationToken);
            return new EmailTransportStatus(true, true, true,
                $"Connected to {_options.Host}:{_options.Port} as {sender}. Mail will be delivered.");
        }
        catch (AuthenticationException exception)
        {
            // Gmail's own words are the most useful thing here: "535-5.7.8 ... BadCredentials" tells
            // an operator immediately that the App Password is wrong rather than the address.
            return new EmailTransportStatus(true, true, false,
                $"{_options.Host} refused the credentials for {_options.Username} — {exception.Message.Trim()}");
        }
#pragma warning disable CA1031
        catch (Exception exception)
#pragma warning restore CA1031
        {
            return new EmailTransportStatus(true, true, false,
                $"Authentication against {_options.Host} failed — {exception.Message}");
        }
        finally
        {
            if (client.IsConnected)
                await client.DisconnectAsync(quit: true, cancellationToken);
        }
    }
}

/// <summary>The counterpart for the Logging provider, which writes mail to the log and never fails.</summary>
internal sealed class LoggingTransportProbe : IEmailTransportProbe
{
    public Task<EmailTransportStatus> CheckAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new EmailTransportStatus(true, true, true,
            "Email:Provider is 'Logging'. Messages are written to the log and delivered to nobody."));
}
