using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Khadra.Application.Common.Ports;
using Khadra.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Notifications;

/// <summary>
/// Sends through Brevo's HTTPS API instead of Brevo's SMTP relay.
///
/// Same account, same verified sender, same free allowance — a different door. The SMTP relay needs
/// outbound 587, and a network that drops it fails in the worst possible way: the TCP handshake
/// succeeds, the greeting never arrives, and every message stalls until it times out. Measured on the
/// network this was written on, one connection in nine got as far as the greeting. Port 443 is not
/// filtered anywhere that HTTPS works at all, which is the whole reason this class exists.
///
/// Preferred over <see cref="ResendEmailSender"/> when mail must reach ADDRESSES THE ACCOUNT DOES NOT
/// OWN. Resend's free tier delivers only to the account owner until a domain is verified; Brevo asks
/// only that the SENDER is verified, so a confirmed single sender can write to any recipient.
///
/// The API key is never in source. It arrives as <c>Email:ApiKey</c> from user-secrets or the
/// environment. Note it is NOT the SMTP key: Brevo issues <c>xkeysib-</c> for this API and
/// <c>xsmtpsib-</c> for the relay, and each is refused by the other.
/// </summary>
internal sealed class BrevoEmailSender(
    IHttpClientFactory httpClientFactory,
    IOptions<EmailOptions> options) : IEmailSender
{
    public const string HttpClientName = "brevo";

    /// <summary>Brevo authenticates on its own header, not <c>Authorization</c>.</summary>
    public const string ApiKeyHeader = "api-key";

    private readonly EmailOptions _options = options.Value;

    private sealed record Party(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("name")] string? Name);

    /// <summary>The shape Brevo's POST /v3/smtp/email expects.</summary>
    private sealed record BrevoRequest(
        [property: JsonPropertyName("sender")] Party Sender,
        [property: JsonPropertyName("to")] Party[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("htmlContent")] string HtmlContent,
        [property: JsonPropertyName("textContent")] string TextContent);

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException(
                "Email:ApiKey is not set, so Brevo cannot be called. Create one at " +
                "app.brevo.com/settings/keys/api and set it in user-secrets or the environment " +
                "(Email__ApiKey). It starts with 'xkeysib-'; the 'xsmtpsib-' SMTP key is refused here.");
        }

        if (string.IsNullOrWhiteSpace(_options.FromAddress))
        {
            throw new InvalidOperationException(
                "Email:FromAddress is not set. Brevo requires a sender it has verified — the address " +
                "confirmed under Senders, Domains & Dedicated IPs.");
        }

        var client = httpClientFactory.CreateClient(HttpClientName);
        var request = new BrevoRequest(
            new Party(_options.FromAddress, string.IsNullOrWhiteSpace(_options.FromName) ? null : _options.FromName),
            [new Party(message.ToAddress, message.ToName)],
            message.Subject,
            message.HtmlBody,
            message.TextBody);

        var response = await client.PostAsJsonAsync("v3/smtp/email", request, cancellationToken);
        if (response.IsSuccessStatusCode)
            return;

        // Brevo names its refusals in the body, and they are nearly always fixable: an unverified
        // sender, the wrong key kind, the daily allowance spent. Carrying that text out is the
        // difference between one operator-readable line and "email failed". The dispatcher catches
        // this and reports the send as failed; it never reaches the person on the form.
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException(
            $"Brevo refused the message ({(int)response.StatusCode} {response.ReasonPhrase}): {detail}");
    }
}
