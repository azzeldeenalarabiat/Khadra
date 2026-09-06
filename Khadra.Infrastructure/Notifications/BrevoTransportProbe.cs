using System.Net;
using System.Text.Json;
using Khadra.Application.Common.Ports;
using Khadra.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Notifications;

/// <summary>
/// Asks Brevo whether the API key is good and the sender is confirmed, without emailing anybody.
///
/// `GET /v3/senders` is an authenticated read that delivers no mail and answers both questions at
/// once: 401 means the key is wrong, and the list says whether the configured From address is one
/// Brevo will actually send as. An unconfirmed sender is the commonest way a correct key still
/// delivers nothing, and Brevo only says so at send time, one message at a time — so saying it at
/// startup turns a per-registration mystery into one line in the log.
/// </summary>
internal sealed class BrevoTransportProbe(
    IHttpClientFactory httpClientFactory,
    IOptions<EmailOptions> options) : IEmailTransportProbe
{
    private readonly EmailOptions _options = options.Value;

    public async Task<EmailTransportStatus> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return new EmailTransportStatus(false, false, false,
                "No Email:ApiKey is set, so Brevo will not be called. Create one at " +
                "app.brevo.com/settings/keys/api and set it with: dotnet user-secrets set " +
                "\"Email:ApiKey\" \"xkeysib-...\" --project Khadra.WebAPI");
        }

        if (_options.ApiKey.StartsWith("xsmtpsib-", StringComparison.OrdinalIgnoreCase))
        {
            // Worth its own sentence: the two keys look alike, live in the same account, and the
            // wrong one fails with a bare 401 that says nothing about which is which.
            return new EmailTransportStatus(false, false, false,
                "Email:ApiKey is Brevo's SMTP key ('xsmtpsib-'), which this API refuses. The HTTP API " +
                "needs the key beginning 'xkeysib-' from app.brevo.com/settings/keys/api.");
        }

        if (string.IsNullOrWhiteSpace(_options.FromAddress))
        {
            return new EmailTransportStatus(false, false, false,
                "No Email:FromAddress is set. Use the address you confirmed under Brevo's Senders, " +
                "Domains & Dedicated IPs.");
        }

        var client = httpClientFactory.CreateClient(BrevoEmailSender.HttpClientName);

        try
        {
            var response = await client.GetAsync("v3/senders", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var senders = await ReadActiveSendersAsync(response, cancellationToken);
                if (senders.Contains(_options.FromAddress, StringComparer.OrdinalIgnoreCase))
                {
                    return new EmailTransportStatus(true, true, true,
                        $"Brevo accepted the API key over HTTPS. Sending as {_options.FromAddress}, " +
                        "a confirmed sender, so mail will be delivered to any recipient.");
                }

                var known = senders.Count == 0 ? "none" : string.Join(", ", senders);
                return new EmailTransportStatus(true, true, false,
                    $"Brevo accepted the API key, but {_options.FromAddress} is NOT a confirmed sender " +
                    $"on this account, so every message will be refused. Confirmed senders: {known}. " +
                    "Add or confirm it under Senders, Domains & Dedicated IPs, or set Email:FromAddress " +
                    "to one of those.");
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return new EmailTransportStatus(true, true, false,
                    "Brevo rejected the API key (401). Check Email:ApiKey — the HTTP API needs the " +
                    "'xkeysib-' key, not the 'xsmtpsib-' SMTP one.");
            }

            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            return new EmailTransportStatus(true, true, false,
                $"Brevo answered {(int)response.StatusCode} {response.ReasonPhrase}: {detail}");
        }
#pragma warning disable CA1031 // Every failure here is reported, never thrown at startup.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            return new EmailTransportStatus(true, false, false,
                $"Could not reach the Brevo API — {exception.Message}");
        }
    }

    /// <summary>The addresses Brevo will currently send as, empty on a fresh account.</summary>
    private static async Task<IReadOnlyCollection<string>> ReadActiveSendersAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("senders", out var senders)
                || senders.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return [.. senders.EnumerateArray()
                .Where(entry => !entry.TryGetProperty("active", out var active)
                    || active.ValueKind != JsonValueKind.False)
                .Select(entry => entry.TryGetProperty("email", out var email) ? email.GetString() : null)
                .Where(email => !string.IsNullOrWhiteSpace(email))
                .Select(email => email!)];
        }
#pragma warning disable CA1031 // A probe never fails because it could not read a hint.
        catch (Exception)
#pragma warning restore CA1031
        {
            return [];
        }
    }
}
