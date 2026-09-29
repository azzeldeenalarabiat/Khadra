using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Khadra.Application.Common;
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
    IOptions<EmailOptions> options,
    IClock clock) : IEmailSender
{
    public const string HttpClientName = "resend";

    private readonly EmailOptions _options = options.Value;

    /// <summary>The shape Resend's POST /emails expects. Snake case is theirs, not ours.</summary>
    private sealed record ResendRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("html")] string Html,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("attachments"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ResendAttachment[]? Attachments,
        [property: JsonPropertyName("reply_to"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? ReplyTo);

    /// <summary>One attached file, its bytes in base64 as Resend takes them.</summary>
    private sealed record ResendAttachment(
        [property: JsonPropertyName("filename")] string FileName,
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("content_type")] string ContentType);

    public async Task<EmailSendReceipt> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
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

        // Resend keeps a key for a day: the same key and payload answer as the first time and send nothing new.
        using var request = new HttpRequestMessage(HttpMethod.Post, "emails")
        {
            Content = JsonContent.Create(new ResendRequest(
                from,
                [message.ToAddress],
                message.Subject,
                message.HtmlBody,
                message.TextBody,
                message.Attachments.Count == 0
                    ? null
                    : [.. message.Attachments.Select(file => new ResendAttachment(file.FileName, Convert.ToBase64String(file.Content), file.ContentType))],
                string.IsNullOrWhiteSpace(message.ReplyTo) ? null : message.ReplyTo.Trim())),
        };
        // …and the same key with any OTHER payload is refused (409). The message's key covers what it says and to whom;
        // the sender is this transport's, so its tag is added here — or a sender changed between a crashed send and its
        // retry would turn every retry into that refusal.
        if (!string.IsNullOrWhiteSpace(message.IdempotencyKey))
            request.Headers.Add("Idempotency-Key", $"{message.IdempotencyKey}-{SenderTag(from)}");

        using var response = await client.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            // Resend answers `{"id":"…"}`, the id its email log is searched by. Like any provider's, it
            // proves the message was ACCEPTED, not that it arrived. Read without the caller's token for
            // the reason BrevoEmailSender gives: an accepted send must not be reported as a failure.
            var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
            return new EmailSendReceipt(
                EmailOptions.ResendProvider,
                ProviderReply.JsonString(body, "id"),
                clock.UtcNow,
                Attempts: 1,
                ProviderResponse: null);
        }

        // Resend explains its refusals in the body — an unverified sender, an invalid key, a domain
        // that is not yours. Carrying that text out is the difference between a fixable error and
        // "email failed". The dispatcher catches this and reports the send as failed; it never
        // reaches the person registering. It is logged at Error, and Resend's free-tier refusal quotes
        // the account owner's own mailbox, so every address in it comes out first.
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException(
            $"Resend refused the message ({(int)response.StatusCode} {response.ReasonPhrase}): " +
            ProviderReply.Refusal(detail));
    }

    /// <summary>Eight hex characters of the sender's hash: the same sender, the same tag.</summary>
    internal static string SenderTag(string from) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(from)))[..8];
}
