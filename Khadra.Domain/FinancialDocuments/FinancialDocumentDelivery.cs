using Khadra.Domain.Common;

namespace Khadra.Domain.FinancialDocuments;

/// <summary>How an issued document reaches its customer outside the platform (payments Phase 7). Email, today.</summary>
public sealed class FinancialDocumentDeliveryChannel : Enumeration
{
    public static readonly FinancialDocumentDeliveryChannel Email = new(1, "Email");

    private FinancialDocumentDeliveryChannel(int id, string name) : base(id, name)
    {
    }
}

/// <summary>
/// Where one delivery stands (payments Phase 7): <see cref="Queued"/> until the mail provider accepts it
/// (<see cref="Sent"/>), until it is given up after the last send attempt allowed (<see cref="Failed"/>), or until
/// it is decided that there is nothing to send and nobody to send it to (<see cref="Skipped"/>). The last three are
/// final: a delivery that ended stays as it ended, and sending again is a new delivery.
/// </summary>
public sealed class FinancialDocumentDeliveryState : Enumeration
{
    /// <summary>Owed and not sent yet — waiting for its turn, its retry, or its PDF.</summary>
    public static readonly FinancialDocumentDeliveryState Queued = new(1, "Queued");

    /// <summary>ACCEPTED by the mail provider. Not proof it was delivered, and not that anybody read it.</summary>
    public static readonly FinancialDocumentDeliveryState Sent = new(2, "Sent");

    /// <summary>Not sent, on purpose: no verified address, a document voided first, or no transport that delivers.</summary>
    public static readonly FinancialDocumentDeliveryState Skipped = new(3, "Skipped");

    /// <summary>Tried as many times as allowed and never accepted — or unsendable for good.</summary>
    public static readonly FinancialDocumentDeliveryState Failed = new(4, "Failed");

    private FinancialDocumentDeliveryState(int id, string name) : base(id, name)
    {
    }

    public bool IsFinal => this != Queued;
}

/// <summary>Why a queued delivery is waiting instead of sending (payments Phase 7).</summary>
public sealed class FinancialDocumentDeliveryWait : Enumeration
{
    /// <summary>
    /// A PDF the email carries is not drawn yet. The email is never sent without it (owner, 2026-09-29), and it never
    /// FAILS for waiting either: its PDF is late, not refused, and the administrator sees it waiting.
    /// </summary>
    public static readonly FinancialDocumentDeliveryWait PdfNotReady = new(1, "PdfNotReady");

    private FinancialDocumentDeliveryWait(int id, string name) : base(id, name)
    {
    }
}

/// <summary>What one attempt at a delivery came to.</summary>
public sealed class FinancialDocumentDeliveryOutcome : Enumeration
{
    /// <summary>The mail provider accepted the message.</summary>
    public static readonly FinancialDocumentDeliveryOutcome Accepted = new(1, "Accepted");

    /// <summary>It was not accepted, or it could not be sent at all.</summary>
    public static readonly FinancialDocumentDeliveryOutcome Failed = new(2, "Failed");

    /// <summary>It was decided not to send it.</summary>
    public static readonly FinancialDocumentDeliveryOutcome Skipped = new(3, "Skipped");

    private FinancialDocumentDeliveryOutcome(int id, string name) : base(id, name)
    {
    }
}

/// <summary>
/// One issued document, owed to its customer by email (payments Phase 7): the outbox row the email dispatcher works
/// from, and — with its <see cref="Attempts"/> — the delivery history the administrator reads (pre-launch item 172).
/// </summary>
/// <remarks>
/// <para>
/// <b>Owed in the transaction that issues the document</b> (<c>FinancialDocumentIssuing</c>), so a receipt can never
/// exist without its promise to be emailed; an administrator's "email it again" is a new delivery, never a second
/// life for one that ended. At most one is <see cref="FinancialDocumentDeliveryState.Queued"/> per document at a time.
/// </para>
/// <para>
/// <b>A send attempt is spent BEFORE the transport is called</b> (<see cref="BeginSend"/>), and its outcome recorded
/// after: a process that dies between the two has spent it, so the history can never show one attempt for two
/// emails. At least once, not exactly once — the message carries an idempotency key, which the providers that honour
/// one use to drop the repeat.
/// </para>
/// <para>
/// <b>Personal data stays here and nowhere append-only.</b> The address a message went to is on this row, which a
/// future erasure can blank; the attempts, which can never change, carry no address, no name and no message body.
/// </para>
/// </remarks>
public sealed class FinancialDocumentDelivery : AggregateRoot
{
    public const int MaxErrorLength = 300;
    public const int MaxAddressLength = 320;
    public const int MaxLanguagesLength = 10;

    private readonly List<FinancialDocumentDeliveryAttempt> _attempts = [];

    public Id DocumentId { get; private set; }

    public FinancialDocumentDeliveryChannel Channel { get; private set; } = null!;

    public FinancialDocumentDeliveryState State { get; private set; } = null!;

    /// <summary>The administrator who asked for it to be sent again, or null for the email owed at issue.</summary>
    public Id? RequestedByAdminId { get; private set; }

    public DateTimeOffset QueuedAt { get; private set; }

    /// <summary>When the dispatcher may next take it: its turn, its retry, its next look for the PDF — or the lease on a claimed row.</summary>
    public DateTimeOffset NextAttemptAt { get; private set; }

    /// <summary>
    /// How many times a dispatcher has TAKEN it, sends or not — and so the proof of a claim: a dispatcher works a row
    /// only while this is still the count its own claim left, so a claim that ran out and was taken over by another is
    /// never worked by both. Mechanics only; the budget is <see cref="SendAttempts"/>.
    /// </summary>
    public int Claims { get; private set; }

    /// <summary>How many times it was handed to the mail transport, counted before each hand-over.</summary>
    public int SendAttempts { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>The verified address the last send used — the address at the time. Personal data: see the remarks.</summary>
    public string? RecipientAddress { get; private set; }

    /// <summary>The languages the last send was composed in: <c>en</c>, <c>ar</c>, or <c>ar,en</c> for a customer who never chose.</summary>
    public string? Languages { get; private set; }

    /// <summary>What a QUEUED delivery is waiting for; null once it is sent or ends — nothing that ended waits.</summary>
    public FinancialDocumentDeliveryWait? WaitingReason { get; private set; }

    /// <summary>Since when it has waited for <see cref="WaitingReason"/>; set exactly when that is.</summary>
    public DateTimeOffset? WaitingSince { get; private set; }

    /// <summary>Why the last attempt did not end it, or why it ended as it did. Never an address, a name or a body.</summary>
    public string? LastError { get; private set; }

    public IReadOnlyList<FinancialDocumentDeliveryAttempt> Attempts => [.. _attempts.OrderBy(attempt => attempt.Number)];

    private FinancialDocumentDelivery()
    {
    }

    private FinancialDocumentDelivery(Id id) : base(id)
    {
    }

    /// <summary>
    /// Owes <paramref name="document"/> to its customer by email: at issue (<paramref name="requestedByAdminId"/> null),
    /// or because an administrator asked for it again.
    /// </summary>
    public static FinancialDocumentDelivery Queue(FinancialDocument document, Id? requestedByAdminId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!document.Type.IsEmailedToCustomer)
            throw new DomainException($"A {document.Type.Name} is not emailed to the customer.");
        if (requestedByAdminId is { IsEmpty: true })
            throw new DomainException("A delivery asked for by an administrator names the administrator.");

        return new FinancialDocumentDelivery(Id.New())
        {
            DocumentId = document.Id,
            Channel = FinancialDocumentDeliveryChannel.Email,
            State = FinancialDocumentDeliveryState.Queued,
            RequestedByAdminId = requestedByAdminId,
            QueuedAt = now,
            NextAttemptAt = now,
        };
    }

    /// <summary>
    /// Not sent this time, for <paramref name="reason"/>, and looked at again at <paramref name="retryAt"/>. Spends no
    /// send attempt and writes no attempt row: nothing was tried. When it started waiting is kept from the first time.
    /// </summary>
    public void Wait(FinancialDocumentDeliveryWait reason, DateTimeOffset retryAt, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(reason);
        RequireQueued();

        if (WaitingReason != reason)
        {
            WaitingReason = reason;
            WaitingSince = now;
        }

        NextAttemptAt = retryAt;
    }

    /// <summary>
    /// About to hand the message to the transport: spends a send attempt, records where it is going and in which
    /// languages, and holds the row until <paramref name="leaseUntil"/>. Saved BEFORE the transport is called.
    /// </summary>
    public void BeginSend(string recipientAddress, string languages, DateTimeOffset leaseUntil)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(languages);
        RequireQueued();
        if (recipientAddress.Length > MaxAddressLength || languages.Length > MaxLanguagesLength)
            throw new DomainException("A delivery's address or languages are longer than their columns.");

        SendAttempts++;
        RecipientAddress = recipientAddress.Trim();
        Languages = languages;
        StopWaiting();
        NextAttemptAt = leaseUntil;
    }

    /// <summary>The mail provider accepted the message: the delivery is <see cref="FinancialDocumentDeliveryState.Sent"/>.</summary>
    public void RecordAccepted(string provider, string? providerMessageId, Id? englishRenditionId, Id? arabicRenditionId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        // First outcome wins: a dispatcher that raced past the lease cannot turn a Sent into anything else.
        if (State.IsFinal)
            return;

        Append(FinancialDocumentDeliveryOutcome.Accepted, now, null, provider, providerMessageId, englishRenditionId, arabicRenditionId);
        StopWaiting();
        State = FinancialDocumentDeliveryState.Sent;
        CompletedAt = now;
        LastError = null;
    }

    /// <summary>
    /// The transport did not take it. Tried again at <paramref name="retryAt"/>, unless that was the last send attempt
    /// allowed, and then it is <see cref="FinancialDocumentDeliveryState.Failed"/>.
    /// </summary>
    public void RecordFailed(
        string error,
        string? provider,
        Id? englishRenditionId,
        Id? arabicRenditionId,
        int maxSendAttempts,
        DateTimeOffset retryAt,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        if (State.IsFinal)
            return;

        Append(FinancialDocumentDeliveryOutcome.Failed, now, error, provider, null, englishRenditionId, arabicRenditionId);
        StopWaiting();
        LastError = Trim(error);
        if (SendAttempts >= maxSendAttempts)
        {
            State = FinancialDocumentDeliveryState.Failed;
            CompletedAt = now;
            return;
        }

        NextAttemptAt = retryAt;
    }

    /// <summary>Not sent, and never will be: <see cref="FinancialDocumentDeliveryState.Skipped"/>, for <paramref name="reason"/>.</summary>
    public void RecordSkipped(string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (State.IsFinal)
            return;

        Append(FinancialDocumentDeliveryOutcome.Skipped, now, reason, null, null, null, null);
        StopWaiting();
        State = FinancialDocumentDeliveryState.Skipped;
        CompletedAt = now;
        LastError = Trim(reason);
    }

    /// <summary>
    /// Unsendable for good, without anything handed to the transport — a stored PDF that no longer matches its hash:
    /// <see cref="FinancialDocumentDeliveryState.Failed"/>, and no send attempt spent.
    /// </summary>
    public void GiveUp(string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (State.IsFinal)
            return;

        Append(FinancialDocumentDeliveryOutcome.Failed, now, reason, null, null, null, null);
        StopWaiting();
        State = FinancialDocumentDeliveryState.Failed;
        CompletedAt = now;
        LastError = Trim(reason);
    }

    /// <summary>
    /// Whatever it waited for is over: it is being sent, or it has ended. A Skipped or Failed row that still said
    /// "waiting for its PDF" would put a wait above its end in the history the administrator reads.
    /// </summary>
    private void StopWaiting()
    {
        WaitingReason = null;
        WaitingSince = null;
    }

    private void Append(
        FinancialDocumentDeliveryOutcome outcome,
        DateTimeOffset now,
        string? error,
        string? provider,
        string? providerMessageId,
        Id? englishRenditionId,
        Id? arabicRenditionId) =>
        _attempts.Add(FinancialDocumentDeliveryAttempt.Record(
            Id, _attempts.Count + 1, outcome, now, Trim(error), provider, providerMessageId, englishRenditionId, arabicRenditionId));

    private void RequireQueued()
    {
        if (State != FinancialDocumentDeliveryState.Queued)
            throw new DomainException($"A {State.Name} delivery is final; sending again is a new delivery.");
    }

    internal static string? Trim(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length <= MaxErrorLength ? trimmed : trimmed[..MaxErrorLength];
    }
}

/// <summary>
/// One attempt at a delivery (payments Phase 7): a send the transport took or did not, or the decision not to send.
/// Append-only — the history the administrator reads, which nothing rewrites — and so it carries NO address, name or
/// message body: only what happened, when, which provider said so, and which PDFs went with it.
/// </summary>
public sealed class FinancialDocumentDeliveryAttempt : Entity, IAppendOnly
{
    public const int MaxProviderLength = 20;
    public const int MaxProviderMessageIdLength = 200;

    public Id DeliveryId { get; private set; }

    public int Number { get; private set; }

    public FinancialDocumentDeliveryOutcome Outcome { get; private set; } = null!;

    public DateTimeOffset AttemptedAt { get; private set; }

    public string? Error { get; private set; }

    /// <summary>The transport that answered: <c>Brevo</c>, <c>Resend</c>, <c>Smtp</c>. An id is unsearchable without it.</summary>
    public string? Provider { get; private set; }

    public string? ProviderMessageId { get; private set; }

    /// <summary>The PDFs the message carried, as renditions: their hashes are the proof of which bytes went.</summary>
    public Id? EnglishRenditionId { get; private set; }

    public Id? ArabicRenditionId { get; private set; }

    private FinancialDocumentDeliveryAttempt()
    {
    }

    private FinancialDocumentDeliveryAttempt(Id id) : base(id)
    {
    }

    internal static FinancialDocumentDeliveryAttempt Record(
        Id deliveryId,
        int number,
        FinancialDocumentDeliveryOutcome outcome,
        DateTimeOffset attemptedAt,
        string? error,
        string? provider,
        string? providerMessageId,
        Id? englishRenditionId,
        Id? arabicRenditionId)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (number < 1)
            throw new DomainException("A delivery's attempts are numbered from 1.");
        if (provider is { Length: > MaxProviderLength })
            throw new DomainException("An attempt's provider is one of the platform's transports.");

        return new FinancialDocumentDeliveryAttempt(Id.New())
        {
            DeliveryId = deliveryId,
            Number = number,
            Outcome = outcome,
            AttemptedAt = attemptedAt,
            Error = error,
            Provider = provider,
            // The provider's own words, of the provider's own length: kept, cut short rather than refused — the send
            // they describe has already happened.
            ProviderMessageId = providerMessageId is { Length: > MaxProviderMessageIdLength }
                ? providerMessageId[..MaxProviderMessageIdLength]
                : providerMessageId,
            EnglishRenditionId = englishRenditionId,
            ArabicRenditionId = arabicRenditionId,
        };
    }
}
