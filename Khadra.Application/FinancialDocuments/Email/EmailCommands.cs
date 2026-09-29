using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Auditing;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Auditing;
using Khadra.Domain.Common;
using Khadra.Domain.FinancialDocuments;
using Khadra.Domain.FinancialDocuments.Repositories;
using Khadra.Domain.IdentityAccess.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Khadra.Application.FinancialDocuments.Email;

/// <summary>How the document emails are worked (payments Phase 7). Mechanics of a sender, not business rules.</summary>
public interface IFinancialDocumentEmailSettings
{
    /// <summary>How many emails one pass takes.</summary>
    int BatchSize { get; }

    /// <summary>
    /// How long a taken email is held from other dispatchers, renewed just before each send — longer than a send can
    /// take (checked at startup against the transport's own budget).
    /// </summary>
    TimeSpan Lease { get; }

    /// <summary>How many times an email is handed to the transport before it is given up as Failed.</summary>
    int MaxSendAttempts { get; }

    /// <summary>The wait before the send that follows send attempt <paramref name="sendAttempt"/>.</summary>
    TimeSpan RetryDelay(int sendAttempt);

    /// <summary>How long before a queued email looks again for a PDF that is not drawn yet.</summary>
    TimeSpan PdfWaitDelay { get; }

    /// <summary>How long an email may stay queued before the administrator's work queue says so.</summary>
    TimeSpan StaleAfter { get; }

    /// <summary>The customer host an email may link to (<c>App:CustomerAppBaseUrl</c>), or empty while there is none.</summary>
    string? CustomerBaseUrl { get; }

    /// <summary>The configured mail transport's name: <c>Brevo</c>, <c>Resend</c>, <c>Smtp</c> or <c>Logging</c>.</summary>
    string TransportName { get; }

    /// <summary>
    /// False when the transport delivers nothing (<c>Logging</c>): an email is then Skipped rather than reported Sent,
    /// so "Sent" means the same thing on every host.
    /// </summary>
    bool TransportDeliversMail { get; }

    /// <summary>
    /// True when the transport cannot reach a real inbox: SMTP to the local stack's Mailpit. Every other transport that
    /// delivers is a real provider.
    /// </summary>
    bool TransportCapturesMail { get; }

    /// <summary>
    /// Whether a TEST document may be emailed to <paramref name="address"/> through a real provider: only an address on
    /// the test-recipient allowlist (owner, 2026-09-29), so a sandbox receipt never reaches an arbitrary inbox.
    /// </summary>
    bool IsTestRecipient(string address);

    /// <summary>
    /// Where a receipt's replies go: Khadra's support address, <c>FinancialDocuments:Issuer:SupportEmail</c> (owner,
    /// 2026-09-29) — or null while no issuer is configured, when no receipt can be issued either.
    /// </summary>
    string? ReplyTo { get; }

    /// <summary>
    /// Why this host sends no financial-document email at all, or null when it may. Production on Brevo, until its
    /// single-send idempotency is verified (owner, 2026-09-29; pre-launch item 202): the emails wait in the queue,
    /// nothing is sent, and an administrator's request is refused. Nothing else about the API stops.
    /// </summary>
    string? DeliveryDisabledReason { get; }
}

/// <summary>Takes the document emails that are due, for one pass of the email service (payments Phase 7).</summary>
public sealed record ClaimFinancialDocumentEmailsQuery : IQuery<IReadOnlyList<ClaimedFinancialDocumentDelivery>>;

/// <summary>
/// Works ONE document email, in its own scope: sends it, waits for its PDF, or decides not to send it — only while
/// <paramref name="Claims"/> is still the count this pass's claim left on it.
/// </summary>
public sealed record EmailFinancialDocumentCommand(Id DeliveryId, int Claims) : ICommand<FinancialDocumentEmailOutcome>;

/// <summary>Where one email stands after its step.</summary>
public sealed record FinancialDocumentEmailOutcome(string State, bool Waiting)
{
    public static readonly FinancialDocumentEmailOutcome Untouched = new("Untouched", false);
}

/// <summary>An administrator asks for a receipt to be emailed to its customer again, with its PDF (payments Phase 7).</summary>
public sealed record RequestFinancialDocumentEmailCommand(Id DocumentId, Id AdminUserId)
    : ICommand<Result<RequestedFinancialDocumentEmailDto, Error>>;

/// <summary>The email just queued, and the document it is for.</summary>
public sealed record RequestedFinancialDocumentEmailDto(Guid DeliveryId, Guid DocumentId);

public sealed class RequestFinancialDocumentEmailCommandValidator : AbstractValidator<RequestFinancialDocumentEmailCommand>
{
    public RequestFinancialDocumentEmailCommandValidator()
    {
        RuleFor(command => command.DocumentId).Must(id => !id.IsEmpty);
        RuleFor(command => command.AdminUserId).Must(id => !id.IsEmpty);
    }
}

public sealed class ClaimFinancialDocumentEmailsHandler(
    IFinancialDocumentDeliveryRepository deliveries,
    IFinancialDocumentEmailSettings settings,
    IClock clock)
    : IRequestHandler<ClaimFinancialDocumentEmailsQuery, IReadOnlyList<ClaimedFinancialDocumentDelivery>>
{
    public Task<IReadOnlyList<ClaimedFinancialDocumentDelivery>> Handle(ClaimFinancialDocumentEmailsQuery request, CancellationToken cancellationToken) =>
        deliveries.ClaimDueAsync(clock.UtcNow, settings.Lease, settings.BatchSize, cancellationToken);
}

/// <summary>
/// Sends one receipt to its customer by email with its PDF (payments Phase 7; owner, 2026-09-29).
/// </summary>
/// <remarks>
/// <para>
/// <b>Decided at the moment of sending:</b> the customer's verified address and preferred language are read when the
/// email is first handed to the transport, not at issue — their PDF in that language, or both for a customer who never
/// chose; a retry keeps the languages of that first hand-over. A receipt voided before its email went is not sent (its
/// correction is, with the correction's PDFs alone); a customer with no verified address is not written to, whoever
/// asked; a TEST receipt goes through a real provider only to an allowlisted address; and a host whose transport
/// delivers nothing reports Skipped rather than a Sent that went nowhere. Replies go to Khadra's support address.
/// </para>
/// <para>
/// <b>One process per email.</b> A row is worked only while its claim count is the one this pass's claim left, and the
/// count is a concurrency token, so a lease that ran out and was claimed again by another process leaves the row to
/// that process — before and after the send alike.
/// </para>
/// <para>
/// <b>Never without its PDF.</b> A PDF not drawn yet makes the email WAIT — no send attempt spent, no attempt row —
/// and it is looked at again shortly, however long that takes; a stored PDF that no longer matches its record is never
/// sent, and the email is given up.
/// </para>
/// <para>
/// <b>An attempt is spent before the transport is called</b>, and the outcome recorded after under no token of the
/// pass's, as the PDFs' own recording is: an accepted send is recorded even while the process stops. A process that
/// dies in between leaves the attempt spent and the email held until its lease runs out; the retry carries the same
/// idempotency key, which Resend uses to drop the repeat and SMTP carries as the Message-ID. Brevo is sent no key
/// (pre-launch item 202): there, a process that dies mid-send can deliver one receipt twice.
/// </para>
/// </remarks>
public sealed partial class EmailFinancialDocumentHandler(
    IFinancialDocumentDeliveryRepository deliveries,
    IFinancialDocumentRepository documents,
    IFinancialDocumentRenditionRepository renditions,
    IUserRepository users,
    IDocumentStorage storage,
    IEmailSender email,
    IFinancialDocumentEmailSettings settings,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<EmailFinancialDocumentHandler> logger)
    : IRequestHandler<EmailFinancialDocumentCommand, FinancialDocumentEmailOutcome>
{
    public async Task<FinancialDocumentEmailOutcome> Handle(EmailFinancialDocumentCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // A host that sends no financial-document email sends none, whoever asks: the email service does not even start
        // there, and this is the same rule at the last step, so nothing can reach the transport (owner, 2026-09-29).
        if (settings.DeliveryDisabledReason is not null)
            return FinancialDocumentEmailOutcome.Untouched;

        var delivery = await deliveries.GetAsync(request.DeliveryId, cancellationToken);
        // Only while the claim is still this pass's. A lease that ran out before the pass reached the row may have been
        // claimed again by another process, and the row is then that process's to work — never both of theirs.
        if (delivery is null || delivery.State.IsFinal || delivery.Claims != request.Claims)
            return FinancialDocumentEmailOutcome.Untouched;

        var now = clock.UtcNow;
        var document = await documents.GetByIdAsync(delivery.DocumentId, cancellationToken);
        if (document is null)
            return await SkipAsync(delivery, "The document does not exist.", now, cancellationToken);
        if (await documents.IsVoidedAsync(document.Id, cancellationToken))
            return await SkipAsync(delivery, "Voided before it was emailed; its correction is emailed instead.", now, cancellationToken);
        if (!settings.TransportDeliversMail)
            return await SkipAsync(delivery, $"No mail transport delivers from this host (Email:Provider is {settings.TransportName}).", now, cancellationToken);

        // Checked here, as it is sent, for every email: the one owed at issue and an administrator's alike. Never to an
        // address the customer has not verified (owner, 2026-09-29).
        var customer = await users.GetByIdAsync(document.CustomerId, cancellationToken);
        if (customer is null || !customer.IsEmailVerified)
            return await SkipAsync(delivery, "The customer has no verified email address.", now, cancellationToken);

        // A TEST receipt never reaches an arbitrary inbox (owner, 2026-09-29): through a real provider only an address
        // on the test-recipient allowlist is written to, while the local stack's Mailpit takes them all.
        if (document.IsTest && !settings.TransportCapturesMail && !settings.IsTestRecipient(customer.Email.Value))
        {
            return await SkipAsync(
                delivery,
                "A TEST document goes through a real mail provider only to an address on FinancialDocuments:Email:TestRecipients.",
                now,
                cancellationToken);
        }

        var languages = LanguagesOf(delivery, customer.PreferredLanguage);

        var attached = new List<(Language Language, FinancialDocumentRendition Rendition, byte[] Bytes)>();
        // In the email's own order: Arabic first for a customer who never chose, as its text is.
        foreach (var language in languages.Where(Rendering.DocumentPrintLayout.Languages.Contains))
        {
            var rendition = await renditions.CurrentAsync(document.Id, language, RenditionFormat.Pdf, RenditionKind.AsIssued, cancellationToken);
            if (rendition is null)
            {
                delivery.Wait(FinancialDocumentDeliveryWait.PdfNotReady, now.Add(settings.PdfWaitDelay), now);
                return await SaveOwnedAsync(cancellationToken)
                    ? new FinancialDocumentEmailOutcome(delivery.State.Name, Waiting: true)
                    : FinancialDocumentEmailOutcome.Untouched;
            }

            var bytes = await ReadAsync(rendition.StorageKey, cancellationToken);
            if (bytes is null
                || bytes.LongLength != rendition.SizeBytes
                || !string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), rendition.ContentSha256, StringComparison.Ordinal))
            {
                // Renditions are never re-drawn for their template, so this never mends itself: said once, loudly.
                LogRenditionAltered(logger, document.Number, language.Name);
                delivery.GiveUp($"The stored {language.Name} PDF is missing or no longer matches its record (rendition_altered).", now);
                return await SaveOwnedAsync(cancellationToken)
                    ? new FinancialDocumentEmailOutcome(delivery.State.Name, Waiting: false)
                    : FinancialDocumentEmailOutcome.Untouched;
            }

            attached.Add((language, rendition, bytes));
        }

        var text = FinancialDocumentEmailLayout.TryCompose(document, customer.Name.Value, languages, settings.CustomerBaseUrl);
        if (text is null)
        {
            LogUnreadable(logger, document.Number);
            delivery.GiveUp("The stored document could not be laid out for its email (snapshot_unreadable).", now);
            return await SaveOwnedAsync(cancellationToken)
                ? new FinancialDocumentEmailOutcome(delivery.State.Name, Waiting: false)
                : FinancialDocumentEmailOutcome.Untouched;
        }

        var files = attached
            .Select(file => new EmailAttachment($"{document.Number}-{file.Language.Name}.pdf", RenditionFormat.Pdf.ContentType, file.Bytes))
            .ToList();
        var replyTo = settings.ReplyTo;
        var message = new EmailMessage(customer.Email.Value, customer.Name.Value, text.Subject, text.HtmlBody, text.TextBody)
        {
            Attachments = files,
            ReplyTo = replyTo,
            IdempotencyKey = IdempotencyKey(delivery.Id, customer.Email.Value, replyTo, text, attached.Select(file => file.Rendition.ContentSha256)),
        };
        var english = attached.Where(file => file.Language == Language.English).Select(file => (Id?)file.Rendition.Id).FirstOrDefault();
        var arabic = attached.Where(file => file.Language == Language.Arabic).Select(file => (Id?)file.Rendition.Id).FirstOrDefault();

        // The attempt is spent HERE, before the transport sees anything — and the lease renewed from this moment, so it
        // covers the send however long the PDFs took to read. A claim taken over meanwhile spends nothing of this pass's.
        delivery.BeginSend(customer.Email.Value, string.Join(',', languages.Select(language => language.Name)), clock.UtcNow.Add(settings.Lease));
        if (!await SaveOwnedAsync(cancellationToken))
            return FinancialDocumentEmailOutcome.Untouched;

        try
        {
            var receipt = await email.SendAsync(message, cancellationToken);
            delivery.RecordAccepted(receipt.Provider, receipt.ProviderMessageId, english, arabic, clock.UtcNow);
            LogSent(logger, document.Number, delivery.SendAttempts, receipt.Provider);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stopping mid-send: whether it went is unknown. The attempt is spent and the lease holds the email until
            // it runs out; the retry carries the same idempotency key.
            throw;
        }
#pragma warning disable CA1031 // A transport failure is a retry, whatever it threw.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            var later = clock.UtcNow;
            delivery.RecordFailed(
                Describe(failure),
                settings.TransportName,
                english,
                arabic,
                settings.MaxSendAttempts,
                later.Add(settings.RetryDelay(delivery.SendAttempts)),
                later);
            LogSendFailed(logger, document.Number, delivery.SendAttempts, delivery.State.Name, failure.GetType().Name);
        }

        // Past the point of no return: the transport has answered, and its answer is recorded whatever happens now —
        // unless the claim was taken over during the send, which only a process frozen for longer than the lease can
        // suffer (startup makes the lease outlast the transport's whole budget twice over). Then the row is the other
        // process's, and it may send the same message again under the same key.
        if (!await SaveOwnedAsync(CancellationToken.None))
        {
            LogOutcomeLost(logger, document.Number, delivery.SendAttempts);
            return FinancialDocumentEmailOutcome.Untouched;
        }

        return new FinancialDocumentEmailOutcome(delivery.State.Name, Waiting: false);
    }

    /// <summary>
    /// The languages an email is written in: the customer's choice when it is first handed to the transport — both,
    /// Arabic first, for a customer who never chose — and on every retry the languages of that first hand-over, so a
    /// retry is the same message under the same idempotency key. A new delivery reads the choice again.
    /// </summary>
    private static List<Language> LanguagesOf(FinancialDocumentDelivery delivery, Language? preferred)
    {
        if (delivery.SendAttempts > 0 && !string.IsNullOrWhiteSpace(delivery.Languages))
        {
            var known = Enumeration.GetAll<Language>();
            List<Language> frozen =
            [
                .. delivery.Languages
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(name => known.FirstOrDefault(language => language.Name == name))
                    .OfType<Language>(),
            ];
            if (frozen.Count > 0)
                return frozen;
        }

        return preferred is { } chosen ? [chosen] : [Language.Arabic, Language.English];
    }

    private async Task<FinancialDocumentEmailOutcome> SkipAsync(
        FinancialDocumentDelivery delivery,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        delivery.RecordSkipped(reason, now);
        return await SaveOwnedAsync(cancellationToken)
            ? new FinancialDocumentEmailOutcome(delivery.State.Name, Waiting: false)
            : FinancialDocumentEmailOutcome.Untouched;
    }

    /// <summary>
    /// Saves, unless another process has claimed the row since this one read it: the claim count is a concurrency
    /// token and every claim moves it, so the write is refused and nothing of this pass's lands. The row is then the
    /// other process's to work.
    /// </summary>
    private async Task<bool> SaveOwnedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (ConcurrencyConflictException)
        {
            return false;
        }
    }

    private async Task<byte[]?> ReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        await using var stream = await storage.OpenAsync(storageKey, cancellationToken);
        if (stream is null)
            return null;
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    /// <summary>
    /// The same key for the same message and a new one for any other — its recipient, where replies go, its words and
    /// its PDFs — so a provider that honours idempotency keys drops a repeat after a crash but never refuses an email
    /// that genuinely changed (Resend answers 409 to one key reused for a different payload; the transport adds its own
    /// sender to the key).
    /// </summary>
    internal static string IdempotencyKey(
        Id deliveryId,
        string recipient,
        string? replyTo,
        FinancialDocumentEmailText text,
        IEnumerable<string> attachmentHashes)
    {
        var payload = string.Join('\n', [recipient, replyTo ?? string.Empty, text.Subject, text.HtmlBody, text.TextBody, .. attachmentHashes]);
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        return $"fd-{deliveryId.Value:N}-{digest[..16]}";
    }

    /// <summary>
    /// What a failed send is recorded as: a transport's own refusal, already stripped of addresses, with anything shaped
    /// like one removed again; anything else by its type alone — never a message from a layer that did not scrub it.
    /// </summary>
    internal static string Describe(Exception failure) =>
        failure is InvalidOperationException && !string.IsNullOrWhiteSpace(failure.Message)
            ? Address().Replace(failure.Message, "(an address)")
            : failure.GetType().Name;

    [GeneratedRegex(@"[^\s<>""'(),;:]+@[^\s<>""'(),;:]+")]
    private static partial Regex Address();

    [LoggerMessage(2700, LogLevel.Information, "Emailed {Number} (send attempt {Attempt}), accepted by {Provider}.")]
    private static partial void LogSent(ILogger logger, string number, int attempt, string provider);

    [LoggerMessage(2701, LogLevel.Warning, "Emailing {Number} failed (send attempt {Attempt}); the email is {State}. {Failure}")]
    private static partial void LogSendFailed(ILogger logger, string number, int attempt, string state, string failure);

    [LoggerMessage(2702, LogLevel.Error, "The stored {Language} PDF of {Number} is missing or no longer matches its record. Its email is given up; it is never sent without its PDF.")]
    private static partial void LogRenditionAltered(ILogger logger, string number, string language);

    [LoggerMessage(2703, LogLevel.Error, "The stored snapshot of {Number} could not be laid out for its email. The email is given up.")]
    private static partial void LogUnreadable(ILogger logger, string number);

    [LoggerMessage(2704, LogLevel.Error,
        "The outcome of emailing {Number} (send attempt {Attempt}) could not be recorded: another process claimed the email while it was being sent, after this one outlived its lease. That process may send it again under the same idempotency key.")]
    private static partial void LogOutcomeLost(ILogger logger, string number, int attempt);
}

/// <summary>
/// Queues a receipt's email again at an administrator's request (payments Phase 7), audited in the same
/// transaction: a new delivery, never a second life for one that ended — at most one queued at a time. Refused, and
/// nothing queued or audited, on a host that sends no financial-document email (Production on Brevo, pre-launch item
/// 202).
/// </summary>
public sealed partial class RequestFinancialDocumentEmailHandler(
    IFinancialDocumentRepository documents,
    IFinancialDocumentDeliveryRepository deliveries,
    AdminActionRecorder audit,
    IFinancialDocumentEmailSettings settings,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<RequestFinancialDocumentEmailHandler> logger)
    : IRequestHandler<RequestFinancialDocumentEmailCommand, Result<RequestedFinancialDocumentEmailDto, Error>>
{
    public async Task<Result<RequestedFinancialDocumentEmailDto, Error>> Handle(
        RequestFinancialDocumentEmailCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var document = await documents.GetByIdAsync(request.DocumentId, cancellationToken);
        if (document is null)
            return FinancialDocumentErrors.NotFound;
        if (!document.Type.IsEmailedToCustomer)
            return FinancialDocumentErrors.NotEmailed;
        if (await documents.IsVoidedAsync(document.Id, cancellationToken))
            return FinancialDocumentErrors.VoidedNotEmailed;
        if (settings.DeliveryDisabledReason is { } disabled)
        {
            LogRefusedWhileDisabled(logger, document.Number, disabled);
            return FinancialDocumentErrors.EmailDeliveryDisabled;
        }

        if (await deliveries.HasQueuedAsync(document.Id, cancellationToken))
            return FinancialDocumentErrors.EmailAlreadyQueued;

        var delivery = FinancialDocumentDelivery.Queue(document, request.AdminUserId, clock.UtcNow);
        deliveries.Add(delivery);
        // Labelled by the NUMBER, as the void is: an audit entry can never be erased, so it names no customer and no address.
        audit.Record(
            AuditAction.FinancialDocumentEmailRequested,
            AuditEntityType.FinancialDocument,
            document.Id,
            document.Number,
            previousValue: null,
            newValue: null);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintConflictException)
        {
            // Another request queued one a moment earlier: the one-queued-at-a-time index said no.
            return FinancialDocumentErrors.EmailAlreadyQueued;
        }

        return new RequestedFinancialDocumentEmailDto(delivery.Id.Value, document.Id.Value);
    }

    [LoggerMessage(2705, LogLevel.Warning,
        "An administrator asked to email {Number} and was refused: financial-document email delivery is disabled on this server. {Reason}")]
    private static partial void LogRefusedWhileDisabled(ILogger logger, string number, string reason);
}
