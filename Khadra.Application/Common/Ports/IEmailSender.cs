using Khadra.Domain.IdentityAccess;

namespace Khadra.Application.Common.Ports;

public sealed record EmailMessage(string ToAddress, string ToName, string Subject, string HtmlBody, string TextBody)
{
    /// <summary>
    /// Files sent with the message (payments Phase 7): an issued receipt's PDFs. Empty for every other email, and
    /// every transport sends none then.
    /// </summary>
    public IReadOnlyList<EmailAttachment> Attachments { get; init; } = [];

    /// <summary>
    /// The same key for the same message sent again after a crash (payments Phase 7), or null. A provider that honours
    /// one drops the repeat — Resend's <c>Idempotency-Key</c>, for a day — and the SMTP transport makes it the
    /// message's Message-ID, which the big mailboxes collapse. Up to 200 letters, digits and hyphens.
    /// </summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>
    /// Where a reply goes, when not to the sender (payments Phase 7): an issued receipt's replies go to Khadra's support
    /// address (owner, 2026-09-29), never to the no-reply sender. Null for every other email, which sets none.
    /// </summary>
    public string? ReplyTo { get; init; }

    /// <summary>
    /// The part of <see cref="ToAddress"/> after its <c>@</c>: all a log line may say about who a
    /// message was for.
    /// </summary>
    /// <remarks>
    /// The whole address is personal data and stays out of the log. The domain is not, and it still
    /// answers the question an operator has when mail goes missing: is it every message, or every
    /// message to one mailbox provider?
    /// </remarks>
    public string RecipientDomain
    {
        get
        {
            var at = ToAddress.LastIndexOf('@');
            return at >= 0 && at < ToAddress.Length - 1 ? ToAddress[(at + 1)..].Trim() : "(unknown)";
        }
    }
}

/// <summary>
/// A file sent with an email (payments Phase 7). Its name is what the recipient's mail client shows and saves it
/// as, and it travels in a header, so it is letters, digits, dots, hyphens and underscores only — nothing a header
/// could be broken with.
/// </summary>
public sealed partial record EmailAttachment
{
    public EmailAttachment(string fileName, string contentType, byte[] content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentNullException.ThrowIfNull(content);
        if (fileName is null || !SafeName().IsMatch(fileName))
            throw new ArgumentException("An attachment's name is 1 to 100 letters, digits, dots, hyphens and underscores.", nameof(fileName));
        if (content.Length == 0)
            throw new ArgumentException("An attachment has content.", nameof(content));

        FileName = fileName;
        ContentType = contentType;
        Content = content;
    }

    public string FileName { get; }

    public string ContentType { get; }

    public byte[] Content { get; }

    [System.Text.RegularExpressions.GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$")]
    private static partial System.Text.RegularExpressions.Regex SafeName();
}

public interface IEmailSender
{
    /// <summary>Hands a message to the transport.</summary>
    /// <returns>
    /// The transport's receipt. One is returned only for a message the transport ACCEPTED: every
    /// refusal and every failure throws instead.
    /// </returns>
    Task<EmailSendReceipt> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// What a transport said when it took a message: that it ACCEPTED it, which is not that it was delivered.
/// </summary>
/// <remarks>
/// <para>
/// Brevo and Resend answer with an id once the message is queued, and an SMTP relay answers the
/// message itself with a 250. Nobody has spoken to the recipient's mail server at that point. Whether
/// the message arrived is in the provider's own log, found by <see cref="ProviderMessageId"/>, and
/// whether anybody can read it is in the recipient's inbox, or their spam folder. A receipt proves the
/// first link of that chain and nothing after it, which is why the line that logs one says "accepted".
/// </para>
/// <para>
/// It is for operators, so it is logged and goes no further. Nothing on it belongs in an API response:
/// a customer has no use for a mail provider's id.
/// </para>
/// </remarks>
/// <param name="Provider">The transport that accepted it: <c>Brevo</c>, <c>Resend</c>, <c>Smtp</c> or <c>Logging</c>.</param>
/// <param name="ProviderMessageId">The id to search the provider's log, or the recipient's headers, by — when there is one.</param>
/// <param name="AcceptedAt">When the transport's acceptance arrived.</param>
/// <param name="Attempts">The attempt that succeeded, counting from one.</param>
/// <param name="ProviderResponse">
/// The provider's own words, where they add something the id does not: an SMTP relay's queue id, or the
/// Logging transport saying it delivered nothing. Never a message body, a header or a credential.
/// </param>
public sealed record EmailSendReceipt(
    string Provider,
    string? ProviderMessageId,
    DateTimeOffset AcceptedAt,
    int Attempts,
    string? ProviderResponse);

// Builds the auth emails; the raw one-time token is embedded in a link to the client application.
public interface IAuthEmailComposer
{
    EmailMessage EmailVerification(User user, string rawToken);

    EmailMessage PasswordReset(User user, string rawToken);

    EmailMessage PasswordChanged(User user);

    /// <summary>Spec 4.2: a dealer owner invited this person to act for the business.</summary>
    EmailMessage EmployeeInvitation(User user, string dealerName, string rawToken);

    /// <summary>An administrator invited by another. Same accept route, different words.</summary>
    EmailMessage AdminInvitation(User user, string rawToken);
}
