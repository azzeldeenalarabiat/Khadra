using System.Net;
using System.Text.Json;
using System.Net.Http.Headers;
using Khadra.Application.Common.Ports;
using Khadra.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Notifications;

/// <summary>
/// Asks Resend whether the API key is good, without sending anything to anybody.
///
/// `GET /domains` is an authenticated read that costs nothing and delivers no mail: 200 means the
/// key works, 401 means it does not, and anything else is Resend having a bad day. That separation
/// is the point — "email is broken" is three different problems with three different fixes.
/// </summary>
internal sealed class ResendTransportProbe(
    IHttpClientFactory httpClientFactory,
    IOptions<EmailOptions> options) : IEmailTransportProbe
{
    private readonly EmailOptions _options = options.Value;

    public async Task<EmailTransportStatus> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return new EmailTransportStatus(false, false, false,
                "No Email:ApiKey is set, so Resend will not be called. Create a key at " +
                "resend.com/api-keys and set it with: dotnet user-secrets set \"Email:ApiKey\" \"re_...\" " +
                "--project Khadra.WebAPI");
        }

        if (string.IsNullOrWhiteSpace(_options.FromAddress))
        {
            return new EmailTransportStatus(false, false, false,
                "No Email:FromAddress is set. Use onboarding@resend.dev while testing, or a sender on " +
                "a domain you have verified with Resend.");
        }

        var client = httpClientFactory.CreateClient(ResendEmailSender.HttpClientName);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        try
        {
            var response = await client.GetAsync("domains", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                // A valid key is not the same as being able to email anybody. Until a domain is
                // verified, Resend accepts the key but refuses every recipient except the account
                // owner — and it only says so at send time, one recipient at a time. The list of
                // verified domains is the same call that validated the key, so saying it up front
                // costs nothing and turns a per-registration mystery into one line at startup.
                var verified = await ReadVerifiedDomainsAsync(response, cancellationToken);
                var senderDomain = _options.FromAddress[(_options.FromAddress.IndexOf('@') + 1)..];

                if (verified.Contains(senderDomain, StringComparer.OrdinalIgnoreCase))
                {
                    return new EmailTransportStatus(true, true, true,
                        $"Resend accepted the API key. Sending as {_options.FromAddress} on a verified domain. Mail will be delivered to anyone.");
                }

                return new EmailTransportStatus(true, true, true,
                    $"Resend accepted the API key, but NO domain is verified on this account, so " +
                    $"{_options.FromAddress} can only deliver to the Resend account owner’s own address. " +
                    "Every other recipient is refused with a 403. Verify a domain at resend.com/domains " +
                    "and set Email:FromAddress to a sender on it.");
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return new EmailTransportStatus(true, true, false,
                    "Resend rejected the API key (401). Check Email:ApiKey — it should start with 're_'.");
            }

            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            return new EmailTransportStatus(true, true, false,
                $"Resend answered {(int)response.StatusCode} {response.ReasonPhrase}: {detail}");
        }
#pragma warning disable CA1031 // Every failure here is reported, never thrown at startup.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            return new EmailTransportStatus(true, false, false,
                $"Could not reach the Resend API — {exception.Message}");
        }
    }

    /// <summary>The names of the domains Resend has verified, empty on a fresh account.</summary>
    private static async Task<IReadOnlyCollection<string>> ReadVerifiedDomainsAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return [];

            return [.. data.EnumerateArray()
                .Where(entry => entry.TryGetProperty("status", out var status)
                    && string.Equals(status.GetString(), "verified", StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.TryGetProperty("name", out var name) ? name.GetString() : null)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)];
        }
#pragma warning disable CA1031 // A probe never fails because it could not read a hint.
        catch (Exception)
#pragma warning restore CA1031
        {
            return [];
        }
    }
}
