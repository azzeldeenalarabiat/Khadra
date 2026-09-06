using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Khadra.Application.Common.Ports;
using Khadra.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Notifications;

/// <summary>
/// Sends through Resend's HTTPS API instead of SMTP.
///
/// Chosen over an SMTP relay for two reasons that matter here. It needs one credential and no port
/// negotiation, so a network that blocks outbound 587 — common on corporate and cloud networks — is
/// not a problem; and when it refuses a message it says why in the response body, which the platform
/// can log and act on, rather than a numeric SMTP code.
///
/// The API key is never in source. It arrives as <c>Email:ApiKey</c> from user-secrets or the
/// environment, and nothing here writes it anywhere.
/// </summary>
internal sealed class ResendEmailSender(
    IHttpClientFactory httpClientFactory,
    IOptions<EmailOptions> options) : IEmailSender
{
    public const string HttpClientName = "resend";

    private readonly EmailOptions _options = options.Value;

    /// <summary>The shape Resend's POST /emails expects. Snake case is theirs, not ours.</summary>
    private sealed record ResendRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("html")] string Html,
        [property: JsonPropertyName("text")] string Text);

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException(
                "Email:ApiKey is not set, so Resend cannot be called. Set it in user-secrets or the " +
                "environment (Email__ApiKey).");
        }

        if (string.IsNullOrWhiteSpace(_options.FromAddress))
        {
            throw new InvalidOperationException(
                "Email:FromAddress is not set. Resend requires a verified sender, or " +
                "onboarding@resend.dev while testing.");
        }

        // "Name <address>" so the recipient sees the platform, not a bare address.
        var from = string.IsNullOrWhiteSpace(_options.FromName)
            ? _options.FromAddress
            : $"{_options.FromName} <{_options.FromAddress}>";

        var client = httpClientFactory.CreateClient(HttpClientName);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        var response = await client.PostAsJsonAsync(
            "emails",
            new ResendRequest(from, [message.ToAddress], message.Subject, message.HtmlBody, message.TextBody),
            cancellationToken);

        if (response.IsSuccessStatusCode)
            return;

        // Resend explains its refusals in the body — an unverified sender, an invalid key, a domain
        // that is not yours. Carrying that text out is the difference between a fixable error and
        // "email failed". The dispatcher catches this and reports the send as failed; it never
        // reaches the person registering.
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException(
            $"Resend refused the message ({(int)response.StatusCode} {response.ReasonPhrase}): {detail}");
    }
}
