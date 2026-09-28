using Khadra.Application.Common.Ports;
using Khadra.Application.FinancialDocuments.Composition;
using Khadra.Application.FinancialDocuments.ReadModels;
using Khadra.Application.Payments.Financials;
using Khadra.Domain.Common;
using Khadra.Domain.FinancialDocuments;
using Khadra.Domain.FinancialDocuments.Repositories;
using Khadra.Domain.Payments;

namespace Khadra.Application.FinancialDocuments.Issuance;

/// <summary>What preparing a document family found.</summary>
public abstract record Preparation
{
    /// <summary>Everything is in place: compose under the stamp the caller takes.</summary>
    /// <param name="Provider">The money's provider, frozen onto the document: the one marker of test money.</param>
    public sealed record Ready(string Provider, Func<DocumentStamp, FinancialDocumentDraft> Compose) : Preparation;

    /// <summary>A document is owed and cannot be issued: the family goes on hold, for an administrator to see.</summary>
    public sealed record OnHold(IssuanceHoldReason Reason, string Error) : Preparation;

    /// <summary>Nothing is owed after all — already issued, unchanged, or no longer what it was.</summary>
    public sealed record NotOwed(string Why) : Preparation;
}

/// <summary>
/// Gathers what a document is composed from and decides whether it can be (payments Phase 5). Shared by the
/// issuing sweep and by an administrator's correction, so a correction is composed exactly as an original is.
/// </summary>
public sealed class DocumentPreparation(
    IFinancialDocumentFactsReader facts,
    IFinancialDocumentRepository documents,
    IFinancialDocumentSettings settings,
    FinancialDocumentComposer composer)
{
    /// <param name="correcting">
    /// True when composing the correction of a voided document: the family already has a row (the voided
    /// one), and a statement is composed afresh whatever its fingerprint says.
    /// </param>
    public async Task<Preparation> PrepareAsync(
        FinancialDocumentType type,
        Id subjectId,
        Id bookingId,
        DateTimeOffset now,
        bool correcting,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(type);

        var money = await facts.BookingAsync(bookingId, cancellationToken);
        if (money is null)
            return new Preparation.OnHold(IssuanceHoldReason.SnapshotFailed, "The booking could not be read.");

        if (type == FinancialDocumentType.PaymentReceipt)
            return await PaymentReceiptAsync(money, subjectId, cancellationToken);
        if (type == FinancialDocumentType.RefundReceipt)
            return await RefundReceiptAsync(money, subjectId, cancellationToken);
        if (type == FinancialDocumentType.BookingStatement)
            return await StatementAsync(money, now, correcting, cancellationToken);

        throw new ArgumentOutOfRangeException(nameof(type), type.Name, "Not a financial document type.");
    }

    private async Task<Preparation> PaymentReceiptAsync(BookingMoneyFacts money, Id paymentId, CancellationToken cancellationToken)
    {
        var payment = money.Payments.FirstOrDefault(candidate => candidate.Id == paymentId);
        if (payment is null || !payment.Status.IsCaptured)
            return new Preparation.NotOwed("payment_not_captured");

        var (issuer, parties, hold) = await PartiesAsync(money, payment.Provider, cancellationToken);
        if (hold is not null)
            return hold;

        return new Preparation.Ready(
            payment.Provider,
            stamp => composer.PaymentReceipt(new PaymentReceiptFacts(issuer!, parties!, money.Booking, payment, money.Payments), stamp));
    }

    private async Task<Preparation> RefundReceiptAsync(BookingMoneyFacts money, Id refundId, CancellationToken cancellationToken)
    {
        var payment = money.Payments.FirstOrDefault(candidate => candidate.Refunds.Any(refund => refund.Id == refundId));
        var refund = payment?.Refunds.First(candidate => candidate.Id == refundId);
        if (payment is null || refund is null || refund.Status != RefundStatus.Settled)
            return new Preparation.NotOwed("refund_not_settled");

        // A refund receipt belongs to its payment's receipt and never alters it, so that one comes first.
        var receipt = await documents.LatestOfFamilyAsync(FinancialDocumentType.PaymentReceipt, payment.Id, cancellationToken);
        if (receipt is null)
            return new Preparation.NotOwed("payment_receipt_first");

        var ticket = refund.DisputeTicketId is { } ticketId
            ? money.ResolvedTickets.FirstOrDefault(candidate => candidate.Id == ticketId)
            : null;
        var decidedAt = ticket is null ? (DateTimeOffset?)null : ticket.ClosedAt ?? ticket.Resolution?.ResolvedAt;

        var (issuer, parties, hold) = await PartiesAsync(money, payment.Provider, cancellationToken);
        if (hold is not null)
            return hold;

        var paymentReceipt = new DocumentReference(receipt.Id, receipt.Number);
        return new Preparation.Ready(
            payment.Provider,
            stamp => composer.RefundReceipt(
                new RefundReceiptFacts(issuer!, parties!, money.Booking, payment, refund, paymentReceipt, decidedAt),
                stamp));
    }

    private async Task<Preparation> StatementAsync(
        BookingMoneyFacts money,
        DateTimeOffset now,
        bool correcting,
        CancellationToken cancellationToken)
    {
        var booking = money.Booking;
        // Read once, because the receipts are both a checkpoint (a correction among them) and the Documents
        // section: the two can never disagree about which receipts stand.
        var receipts = await documents.ListLatestReceiptsForBookingAsync(booking.Id, cancellationToken);
        var checkpoints = StatementCheckpoints.Of(booking, money.Payments, money.ResolvedTickets, receipts);
        if (checkpoints.IsEmpty)
            return new Preparation.NotOwed("no_money_moved");

        if (!correcting)
        {
            var latest = await documents.LatestOfFamilyAsync(FinancialDocumentType.BookingStatement, booking.Id, cancellationToken);
            if (latest is not null && string.Equals(latest.CheckpointFingerprint, checkpoints.Fingerprint, StringComparison.Ordinal))
                return new Preparation.NotOwed("unchanged");
        }

        // An official record never freezes records that contradict one another; a live screen still shows
        // them, but a statement waits on hold until somebody has put them right.
        var financials = BookingFinancialsCalculator.Calculate(booking, money.Payments, money.ResolvedTickets, money.HasLiveDispute, now);
        if (financials.NeedsReview)
            return new Preparation.OnHold(IssuanceHoldReason.RecordsNeedReview, string.Join(", ", financials.Issues));

        // One database holds one kind of money (PaymentsStartupCheck); a booking whose captures disagree is a
        // defect, and a statement cannot say which kind of money it is about.
        var providers = money.Payments
            .Where(payment => payment.Status.IsCaptured)
            .Select(payment => payment.Provider)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (providers.Count != 1)
            return new Preparation.OnHold(IssuanceHoldReason.SnapshotFailed, "The booking's captured payments name different providers.");

        var provider = providers[0];
        var (issuer, parties, hold) = await PartiesAsync(money, provider, cancellationToken);
        if (hold is not null)
            return hold;

        var references = receipts.Select(receipt => new ReceiptReference(receipt.Type, receipt.Id, receipt.Number)).ToList();
        return new Preparation.Ready(
            provider,
            stamp => composer.Statement(
                new StatementFacts(issuer!, parties!, booking, financials, checkpoints, money.ResolvedTickets.Count > 0, references),
                stamp,
                provider));
    }

    /// <summary>
    /// The issuer and the parties, or the hold that stops the document: no issuer, nothing is issued (owner,
    /// 2026-09-27); a party that does not resolve at all is a defect, never a blank on a document.
    /// </summary>
    private async Task<(DocumentIssuer? Issuer, DocumentParties? Parties, Preparation.OnHold? Hold)> PartiesAsync(
        BookingMoneyFacts money,
        string provider,
        CancellationToken cancellationToken)
    {
        if (settings.Issuer is not { } issuer)
        {
            return (null, null, new Preparation.OnHold(
                IssuanceHoldReason.IssuerNotConfigured,
                "FinancialDocuments:Issuer is not configured."));
        }

        // A test identity signs sandbox money only (owner, 2026-09-27). Startup already refuses it outside
        // Development with the sandbox; this is the second lock, on the money itself.
        if (issuer.IsTestIdentity && !PaymentProviders.IsSandbox(provider))
        {
            return (null, null, new Preparation.OnHold(
                IssuanceHoldReason.IssuerNotConfigured,
                "The configured issuer is a test identity, which never signs real money."));
        }

        var booking = money.Booking;
        var parties = await facts.PartiesAsync(booking.CustomerId, booking.DealerId, booking.VehicleId, cancellationToken);
        return parties is null
            ? (null, null, new Preparation.OnHold(IssuanceHoldReason.SnapshotFailed, "The customer, the rental office or the car could not be read."))
            : (issuer, parties, null);
    }
}

/// <summary>A document could not be composed: a defect in the composition, never a database failure.</summary>
public sealed class DocumentCompositionException : Exception
{
    public DocumentCompositionException()
    {
    }

    public DocumentCompositionException(string message) : base(message)
    {
    }

    public DocumentCompositionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Takes a document's number and issues it (payments Phase 5). MUST run inside the caller's transaction:
/// the number is taken from the series row under a lock, and the document is inserted in the same
/// transaction, so if anything after the number fails the number goes back with the rollback and the series
/// stays gapless.
/// </summary>
public sealed class FinancialDocumentIssuing(
    IFinancialDocumentSeries series,
    IFinancialDocumentRepository documents,
    IReportingCalendar calendar)
{
    public async Task<FinancialDocument> IssueAsync(
        FinancialDocumentType type,
        string provider,
        DateTimeOffset issuedAt,
        int version,
        DocumentReference? previous,
        bool isCorrection,
        Func<DocumentStamp, FinancialDocumentDraft> compose,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(compose);

        // The series follows the Amman ISSUE year and the kind of money: TEST- for sandbox money (owner,
        // 2026-09-27), read from the same frozen provider the document carries.
        var seriesKey = FinancialDocumentNumbers.SeriesKey(type, calendar.DayOf(issuedAt).Year, PaymentProviders.IsSandbox(provider));
        var sequence = await series.TakeNextAsync(seriesKey, issuedAt, cancellationToken);
        var number = FinancialDocumentNumbers.Format(seriesKey, sequence);

        FinancialDocument document;
        try
        {
            var draft = compose(new DocumentStamp(number, issuedAt, version, previous, isCorrection));
            if (draft.Type != type || !string.Equals(draft.Provider, provider, StringComparison.Ordinal))
                throw new InvalidOperationException("The composed document is not the one being issued.");
            document = FinancialDocument.Issue(draft, number, issuedAt);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new DocumentCompositionException($"The {type.Name} could not be composed: {exception.Message}", exception);
        }

        documents.Add(document);
        return document;
    }
}
