using System.Globalization;
using System.Text.Json.Nodes;
using Khadra.Application.Bookings.Dtos;
using Khadra.Application.Common.Dtos;
using Khadra.Application.Common.Ports;
using Khadra.Application.Payments.Financials;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.FinancialDocuments;
using Khadra.Domain.Payments;

namespace Khadra.Application.FinancialDocuments.Composition;

/// <summary>What a payment receipt is composed from.</summary>
/// <param name="BookingPayments">Every payment on the booking, with its refunds: the "paid to date" rule reads them.</param>
public sealed record PaymentReceiptFacts(
    DocumentIssuer Issuer,
    DocumentParties Parties,
    Booking Booking,
    Payment Payment,
    IReadOnlyList<Payment> BookingPayments);

/// <summary>What a refund receipt is composed from.</summary>
/// <param name="PaymentReceipt">The current receipt of the payment the refund returns: the refund receipt links to it and never alters it.</param>
/// <param name="DisputeDecidedAt">When the ticket behind a dispute decision's refund was decided; null for any other refund.</param>
public sealed record RefundReceiptFacts(
    DocumentIssuer Issuer,
    DocumentParties Parties,
    Booking Booking,
    Payment Payment,
    Refund Refund,
    DocumentReference PaymentReceipt,
    DateTimeOffset? DisputeDecidedAt);

/// <summary>What a booking statement is composed from.</summary>
/// <param name="Financials">The ONE financial state, computed at issue. It must not need review: a contradiction is never frozen.</param>
/// <param name="PenaltyResolvedByDispute">Whether a dispute on the booking was resolved (pre-launch item 173).</param>
/// <param name="Receipts">The current version of every receipt issued on the booking so far.</param>
public sealed record StatementFacts(
    DocumentIssuer Issuer,
    DocumentParties Parties,
    Booking Booking,
    BookingFinancials Financials,
    StatementCheckpoints Checkpoints,
    bool PenaltyResolvedByDispute,
    IReadOnlyList<ReceiptReference> Receipts);

/// <summary>
/// Composes the three financial documents (payments Phase 5): a payment receipt, a refund receipt, a
/// booking statement. Pure: facts in, a draft with its canonical snapshot out.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every figure is the server's, computed once.</b> A receipt is built from facts that never change
/// once written — the capture, the fee, the stored refund split — so it reads the same whether it is
/// issued now or a year from now. A statement is <c>BookingFinancialsDto.For(financials, Customer)</c>,
/// the customer's projection, frozen: it is written field by field from that projection and from nothing
/// wider, so commission, the office's and the platform's dispute shares, a charge to the office, provider
/// references, failure codes, orphan reasons and the sandbox marker cannot reach it. A leak into an
/// append-only record could never be taken back.
/// </para>
/// <para>
/// <b>Every word is frozen with the figures</b> (<see cref="DocumentWording"/>), in English and Arabic,
/// so no reader of the document words or computes anything.
/// </para>
/// </remarks>
public sealed class FinancialDocumentComposer(IReportingCalendar calendar)
{
    public FinancialDocumentDraft PaymentReceipt(PaymentReceiptFacts facts, DocumentStamp stamp)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(stamp);
        var (booking, payment) = (facts.Booking, facts.Payment);
        if (!payment.Status.IsCaptured || payment.BookingId != booking.Id)
            throw new InvalidOperationException($"Payment {payment.Id.Value} was not captured on booking {booking.Id.Value}.");

        var capturedAt = CapturedAt(payment);
        var charged = payment.AmountCaptured ?? payment.Amount;
        var applied = payment.Status == PaymentStatus.Applied;
        var sameCurrency = string.Equals(charged.CurrencyCode, booking.Pricing.CurrencyCode, StringComparison.Ordinal);
        var appliedToBooking = applied ? payment.AppliedToBooking : Money.ZeroIn(charged.CurrencyCode);

        // ONE rule for the balance (plan §3.6): the frozen total less the applied money of every payment
        // applied at or before this one, in (applied at, id) order. So a receipt reads the same whenever it
        // is issued. A capture that never applied — or one in another currency — is never set against the
        // booking at all.
        Position? position = null;
        if (applied && sameCurrency)
        {
            var paidToDate = facts.BookingPayments
                .Where(other => other.BookingId == booking.Id && other.Status == PaymentStatus.Applied)
                .Where(other => string.Equals(other.Amount.CurrencyCode, booking.Pricing.CurrencyCode, StringComparison.Ordinal))
                .Where(other => AtOrBefore(other.AppliedAt!.Value, other.Id, payment.AppliedAt!.Value, payment.Id))
                .Aggregate(Money.ZeroIn(booking.Pricing.CurrencyCode), (sum, other) => sum.Add(other.AppliedToBooking));
            var balance = booking.Pricing.TotalPrice.Amount - paidToDate.Amount;
            if (balance < 0m)
                throw new InvalidOperationException(
                    $"Booking {booking.Id.Value}: the payments applied to it exceed its total. No receipt states a negative balance.");
            var balanceAfter = Money.Create(balance, booking.Pricing.CurrencyCode);
            position = new Position(paidToDate, balanceAfter, balanceAfter.IsZero ? BalanceStates.PaidInFull : BalanceStates.DueAtHandover);
        }

        var factsNode = new JsonObject
        {
            ["paymentId"] = SnapshotJson.IdText(payment.Id),
            ["purpose"] = payment.Purpose.Name,
            ["status"] = payment.Status.Name,
            ["capturedAt"] = Instant(capturedAt),
            ["amountCharged"] = SnapshotJson.Money(charged),
            ["processingFee"] = SnapshotJson.Money(payment.ProcessingFee),
            ["feeRefundable"] = payment.FeeRefundable,
            ["appliedToBooking"] = SnapshotJson.Money(appliedToBooking),
            ["bookingPosition"] = position is null
                ? null
                : new JsonObject
                {
                    ["bookingTotal"] = SnapshotJson.Money(booking.Pricing.TotalPrice),
                    ["requiredDeposit"] = SnapshotJson.Money(booking.Pricing.DepositAmount),
                    ["paidOnlineToDate"] = SnapshotJson.Money(position.PaidToDate),
                    ["balanceAfter"] = SnapshotJson.Money(position.BalanceAfter),
                    ["balanceState"] = position.State,
                },
            // The whole capture goes back when it could not be applied (decision 7).
            ["toBeRefunded"] = applied ? null : SnapshotJson.Money(charged),
        };

        var content = new DocumentContent();
        var cause = stamp.IsCorrection ? FinancialDocumentCause.Correction : FinancialDocumentCause.PaymentCaptured;
        DocumentSection(content, stamp, cause, coversUntil: null);
        PartiesSection(content, facts.Issuer, facts.Parties);

        var section = content.Section("payment", DocumentWording.Headings.Payment)
            .Text("purpose", DocumentWording.Labels.PaymentFor, applied ? DocumentWording.Purpose(payment.Purpose) : DocumentWording.NotApplied)
            .Instant("paidOn", DocumentWording.Labels.PaidOn, capturedAt, calendar)
            .Money("amountPaid", DocumentWording.Labels.AmountPaid, charged);
        if (!payment.ProcessingFee.IsZero)
        {
            section.Money("processingFee", DocumentWording.Labels.ProcessingFee, payment.ProcessingFee)
                .Text("feeRefundable", DocumentWording.Labels.FeeRefundability, DocumentWording.FeeRefundable(payment.FeeRefundable));
        }

        if (applied)
            section.Money("appliedToBooking", DocumentWording.Labels.AppliedToBooking, appliedToBooking);
        else
            section.Text("notApplied", null, DocumentWording.OrphanNote).Money("toBeRefunded", DocumentWording.Labels.ToBeRefunded, charged);

        if (position is not null)
        {
            var yours = content.Section("bookingPosition", DocumentWording.Headings.YourBooking)
                .Money("bookingTotal", DocumentWording.Labels.BookingTotal, booking.Pricing.TotalPrice)
                .Money("requiredDeposit", DocumentWording.Labels.RequiredDeposit, booking.Pricing.DepositAmount)
                .Money("paidOnlineToDate", DocumentWording.Labels.PaidOnlineToDate, position.PaidToDate);
            if (position.State == BalanceStates.PaidInFull)
                yours.Money("balanceAfter", DocumentWording.Labels.RemainingBalance, position.BalanceAfter)
                    .Text("balanceState", null, DocumentWording.PaidInFull);
            else
                yours.Money("balanceAfter", DocumentWording.Labels.BalanceDueAtPickup, position.BalanceAfter);
        }

        BookingSection(content, booking, facts.Parties);

        var snapshot = Root(
            FinancialDocumentType.PaymentReceipt, stamp, cause, capturedAt, charged.CurrencyCode,
            facts.Issuer, facts.Parties, booking, factsNode,
            content.Build(DocumentWording.Title(FinancialDocumentType.PaymentReceipt), DocumentWording.Labels.AmountPaid, charged));

        return new FinancialDocumentDraft(
            FinancialDocumentType.PaymentReceipt,
            payment.Id,
            stamp.Version,
            stamp.Previous?.DocumentId,
            RelatedDocumentId: null,
            booking.Id,
            booking.Reference.Value,
            booking.CustomerId,
            booking.DealerId,
            payment.Id,
            RefundId: null,
            cause,
            capturedAt,
            CoversThrough: null,
            CheckpointFingerprint: null,
            charged,
            payment.Provider,
            BookingFinancials.CalculatorVersion,
            SnapshotJson.SchemaVersion,
            SnapshotJson.Serialize(snapshot));
    }

    public FinancialDocumentDraft RefundReceipt(RefundReceiptFacts facts, DocumentStamp stamp)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(stamp);
        var (booking, payment, refund) = (facts.Booking, facts.Payment, facts.Refund);
        if (payment.BookingId != booking.Id || payment.Refunds.All(own => own.Id != refund.Id))
            throw new InvalidOperationException($"Refund {refund.Id.Value} is not one of payment {payment.Id.Value}'s on booking {booking.Id.Value}.");
        if (refund.Status != RefundStatus.Settled || refund.SettledAt is not { } settledAt)
            throw new InvalidOperationException($"Refund {refund.Id.Value} is not settled: a refund receipt is issued once the money is back.");

        var charged = payment.AmountCaptured ?? payment.Amount;
        var capturedAt = CapturedAt(payment);

        // Settled refunds on the payment up to and including this one, in (settled at, id) order: what the
        // receipt says has come back from the payment so far, the same whenever it is issued.
        var refundedToDate = payment.Refunds
            .Where(other => other.Status == RefundStatus.Settled && other.SettledAt is not null)
            .Where(other => string.Equals(other.Amount.CurrencyCode, refund.Amount.CurrencyCode, StringComparison.Ordinal))
            .Where(other => AtOrBefore(other.SettledAt!.Value, other.Id, settledAt, refund.Id))
            .Aggregate(Money.ZeroIn(refund.Amount.CurrencyCode), (sum, other) => sum.Add(other.Amount));

        var factsNode = new JsonObject
        {
            ["refundId"] = SnapshotJson.IdText(refund.Id),
            ["paymentId"] = SnapshotJson.IdText(payment.Id),
            ["reason"] = refund.Reason.Name,
            ["amount"] = SnapshotJson.Money(refund.Amount),
            ["bookingPart"] = SnapshotJson.Money(refund.BookingPart),
            ["feePart"] = SnapshotJson.Money(refund.FeePart),
            ["recordedAt"] = Instant(refund.RequestedAt),
            ["settledAt"] = Instant(settledAt),
            ["disputeDecidedAt"] = SnapshotJson.OptionalInstant(facts.DisputeDecidedAt, calendar),
            ["payment"] = new JsonObject
            {
                ["purpose"] = payment.Purpose.Name,
                ["status"] = payment.Status.Name,
                ["capturedAt"] = Instant(capturedAt),
                ["amountCharged"] = SnapshotJson.Money(charged),
                ["receipt"] = SnapshotJson.Reference(facts.PaymentReceipt),
            },
            ["refundedToDateOnPayment"] = SnapshotJson.Money(refundedToDate),
        };

        var content = new DocumentContent();
        var cause = stamp.IsCorrection ? FinancialDocumentCause.Correction : FinancialDocumentCause.RefundSettled;
        DocumentSection(content, stamp, cause, coversUntil: null);
        PartiesSection(content, facts.Issuer, facts.Parties);

        var section = content.Section("refund", DocumentWording.Headings.Refund)
            .Text("reason", DocumentWording.Labels.Reason, DocumentWording.RefundReason(refund.Reason))
            .Money("amountRefunded", DocumentWording.Labels.AmountRefunded, refund.Amount);
        if (!refund.FeePart.IsZero)
        {
            section.Money("bookingPart", DocumentWording.Labels.BookingMoneyInRefund, refund.BookingPart)
                .Money("feePart", DocumentWording.Labels.FeeInRefund, refund.FeePart);
        }

        section.Instant("recordedAt", DocumentWording.Labels.RefundRecordedAt, refund.RequestedAt, calendar)
            .Instant("refundedAt", DocumentWording.Labels.RefundedAt, settledAt, calendar);
        if (facts.DisputeDecidedAt is { } decidedAt)
            section.Instant("disputeDecidedAt", DocumentWording.Labels.DisputeDecidedAt, decidedAt, calendar);
        section.Text("bankNote", null, DocumentWording.BankMayTakeTime);

        content.Section("originalPayment", DocumentWording.Headings.OriginalPayment)
            .Text(
                "purpose",
                DocumentWording.Labels.PaymentFor,
                payment.Status == PaymentStatus.Applied ? DocumentWording.Purpose(payment.Purpose) : DocumentWording.NotApplied)
            .Instant("paidOn", DocumentWording.Labels.PaidOn, capturedAt, calendar)
            .Money("amountPaid", DocumentWording.Labels.AmountPaid, charged)
            .Plain("paymentReceipt", DocumentWording.Labels.PaymentReceipt, facts.PaymentReceipt.Number)
            .Money("refundedToDate", DocumentWording.Labels.RefundedFromPaymentToDate, refundedToDate);

        BookingSection(content, booking, facts.Parties);

        var snapshot = Root(
            FinancialDocumentType.RefundReceipt, stamp, cause, settledAt, refund.Amount.CurrencyCode,
            facts.Issuer, facts.Parties, booking, factsNode,
            content.Build(DocumentWording.Title(FinancialDocumentType.RefundReceipt), DocumentWording.Labels.AmountRefunded, refund.Amount));

        return new FinancialDocumentDraft(
            FinancialDocumentType.RefundReceipt,
            refund.Id,
            stamp.Version,
            stamp.Previous?.DocumentId,
            facts.PaymentReceipt.DocumentId,
            booking.Id,
            booking.Reference.Value,
            booking.CustomerId,
            booking.DealerId,
            payment.Id,
            refund.Id,
            cause,
            settledAt,
            CoversThrough: null,
            CheckpointFingerprint: null,
            refund.Amount,
            payment.Provider,
            BookingFinancials.CalculatorVersion,
            SnapshotJson.SchemaVersion,
            SnapshotJson.Serialize(snapshot));
    }

    /// <param name="provider">The booking's payments' provider: the one marker of test money, frozen onto the row.</param>
    public FinancialDocumentDraft Statement(StatementFacts facts, DocumentStamp stamp, string provider)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(stamp);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        var (booking, financials, checkpoints) = (facts.Booking, facts.Financials, facts.Checkpoints);
        if (financials.BookingId != booking.Id)
            throw new InvalidOperationException("The financial state is another booking's.");
        if (financials.NeedsReview)
            throw new InvalidOperationException($"Booking {booking.Id.Value}: its records contradict one another; no statement freezes a contradiction.");
        if (checkpoints.IsEmpty)
            throw new InvalidOperationException($"Booking {booking.Id.Value}: no money has moved, so there is nothing to state.");

        // The customer's projection and nothing wider (decision 3, plan §3.10).
        var view = BookingFinancialsDto.For(financials, BookingParty.Customer);
        var currency = view.Currency;
        var latest = checkpoints.Latest;
        var cause = stamp.IsCorrection ? FinancialDocumentCause.Correction : latest.Kind;
        var net = Money.Create(view.Summary.ChargedOnline!.Amount - view.Summary.Refunded.Amount, currency);
        var penalty = booking.HasPenaltyAgainstCustomer ? booking.Penalty : null;

        var factsNode = new JsonObject
        {
            ["bookingStatus"] = view.BookingStatus,
            ["summary"] = SummaryFacts(view.Summary, net),
            ["balance"] = BalanceFacts(view.Balance),
            ["deposit"] = DepositFacts(view.Deposit),
            ["penalty"] = penalty is null
                ? null
                : new JsonObject
                {
                    ["minAmount"] = SnapshotJson.Money(penalty.MinAmount),
                    ["maxAmount"] = SnapshotJson.Money(penalty.MaxAmount),
                    ["reasonCode"] = penalty.ReasonCode?.Name,
                    ["standing"] = facts.PenaltyResolvedByDispute ? PenaltyStates.ResolvedByDispute : PenaltyStates.Assessed,
                },
            ["payments"] = new JsonArray([.. view.Payments.Select(PaymentFacts)]),
            ["receipts"] = new JsonArray(
            [
                .. facts.Receipts.Select(receipt => (JsonNode)new JsonObject
                {
                    ["type"] = receipt.Type.Name,
                    ["documentId"] = SnapshotJson.IdText(receipt.DocumentId),
                    ["number"] = receipt.Number,
                }),
            ]),
        };

        var content = new DocumentContent();
        DocumentSection(content, stamp, cause, checkpoints.CoversThrough);
        PartiesSection(content, facts.Issuer, facts.Parties);
        BookingSection(content, booking, facts.Parties);
        ChargesSection(content, view.Summary);
        var index = 0;
        foreach (var payment in view.Payments)
            PaymentSection(content, payment, ++index);
        RefundsSection(content, view.Payments);
        DepositSection(content, view.Deposit);
        if (penalty is not null)
        {
            content.Section("penalty", DocumentWording.Headings.Penalty)
                .Add(section => penalty.IsRange
                    ? section.Text("amountAssessed", DocumentWording.Labels.AmountAssessed, DocumentWording.Range(penalty.MinAmount, penalty.MaxAmount))
                    : section.Money("amountAssessed", DocumentWording.Labels.AmountAssessed, penalty.MaxAmount))
                .Text("reason", DocumentWording.Labels.Reason, DocumentWording.PenaltyReason(penalty.ReasonCode, penalty.Reason))
                .Text("standing", null, DocumentWording.PenaltyStanding(facts.PenaltyResolvedByDispute));
        }

        BalanceSection(content, view.Balance);
        TotalsSection(content, view.Summary, net);
        DocumentsSection(content, facts.Receipts, stamp);

        var snapshot = Root(
            FinancialDocumentType.BookingStatement, stamp, cause, latest.At, currency,
            facts.Issuer, facts.Parties, booking, factsNode,
            content.Build(DocumentWording.Title(FinancialDocumentType.BookingStatement), DocumentWording.Labels.NetPaidOnline, net));

        return new FinancialDocumentDraft(
            FinancialDocumentType.BookingStatement,
            booking.Id,
            stamp.Version,
            stamp.Previous?.DocumentId,
            RelatedDocumentId: null,
            booking.Id,
            booking.Reference.Value,
            booking.CustomerId,
            booking.DealerId,
            PaymentId: null,
            RefundId: null,
            cause,
            latest.At,
            checkpoints.CoversThrough,
            checkpoints.Fingerprint,
            net,
            provider,
            BookingFinancials.CalculatorVersion,
            SnapshotJson.SchemaVersion,
            SnapshotJson.Serialize(snapshot));
    }

    /// <summary>When a capture happened: applied, or orphaned, or merely captured.</summary>
    public static DateTimeOffset CapturedAt(Payment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);
        return payment.AppliedAt ?? payment.OrphanedAt ?? payment.CapturedAt
            ?? throw new InvalidOperationException($"Payment {payment.Id.Value} has no capture instant.");
    }

    // ── The parts every document shares ────────────────────────────────────────────────────────────

    private JsonObject Root(
        FinancialDocumentType type,
        DocumentStamp stamp,
        FinancialDocumentCause cause,
        DateTimeOffset occurredAt,
        string currency,
        DocumentIssuer issuer,
        DocumentParties parties,
        Booking booking,
        JsonObject facts,
        JsonObject content)
    {
        var (customer, office, vehicle) = (parties.Customer, parties.Office, parties.Vehicle);
        if (customer.CustomerId != booking.CustomerId || office.DealerId != booking.DealerId || vehicle.VehicleId != booking.VehicleId)
            throw new InvalidOperationException($"The parties read are not booking {booking.Id.Value}'s.");

        return new JsonObject
        {
            ["schemaVersion"] = SnapshotJson.SchemaVersion,
            ["document"] = new JsonObject
            {
                ["type"] = type.Name,
                ["number"] = stamp.Number,
                ["version"] = stamp.Version,
                ["cause"] = cause.Name,
                ["occurredAt"] = Instant(occurredAt),
                ["issuedAt"] = Instant(stamp.IssuedAt),
                ["timeZone"] = calendar.TimeZoneId,
                ["calculatorVersion"] = BookingFinancials.CalculatorVersion,
                ["currency"] = new JsonObject
                {
                    ["code"] = currency,
                },
                // The decimals every amount in this document is written with: the platform's own scale for
                // every currency it stores, not the currency's — a USD amount is written "50.000" too.
                ["amountScale"] = Money.MinorUnits,
                ["previous"] = stamp.Previous is null ? null : SnapshotJson.Reference(stamp.Previous),
                ["isCorrection"] = stamp.IsCorrection,
            },
            ["issuer"] = new JsonObject
            {
                ["legalName"] = SnapshotJson.Text(issuer.LegalName),
                ["commercialRegistration"] = issuer.CommercialRegistration,
                ["address"] = SnapshotJson.Text(issuer.Address),
                ["supportEmail"] = issuer.SupportEmail,
                ["supportPhone"] = issuer.SupportPhone,
            },
            // The name only (owner, 2026-09-27): no email and no phone in a record nothing can erase.
            ["customer"] = new JsonObject
            {
                ["customerId"] = SnapshotJson.IdText(customer.CustomerId),
                ["name"] = customer.Name,
            },
            ["office"] = new JsonObject
            {
                ["dealerId"] = SnapshotJson.IdText(office.DealerId),
                ["name"] = office.Name,
                ["commercialRegistration"] = office.CommercialRegistration,
                ["city"] = SnapshotJson.OptionalText(office.City),
                ["area"] = office.Area,
                ["street"] = office.Street,
            },
            ["booking"] = new JsonObject
            {
                ["bookingId"] = SnapshotJson.IdText(booking.Id),
                ["reference"] = booking.Reference.Value,
                ["statusAtIssue"] = booking.Status.Name,
                ["rentalStart"] = Instant(booking.Period.Start),
                ["rentalEnd"] = Instant(booking.Period.End),
                ["days"] = booking.Pricing.Days,
                ["pickupMethod"] = booking.PickupMethod.Name,
                ["vehicle"] = new JsonObject
                {
                    ["make"] = vehicle.Make,
                    ["model"] = vehicle.Model,
                    ["year"] = vehicle.Year,
                    ["plate"] = vehicle.Plate,
                    ["carType"] = SnapshotJson.OptionalText(vehicle.CarType),
                },
            },
            ["facts"] = facts,
            ["content"] = content,
        };
    }

    private void DocumentSection(DocumentContent content, DocumentStamp stamp, FinancialDocumentCause cause, DateTimeOffset? coversUntil)
    {
        var section = content.Section("document", DocumentWording.Headings.Document)
            .Plain("number", DocumentWording.Labels.Number, stamp.Number)
            .Plain("version", DocumentWording.Labels.Version, stamp.Version.ToString(CultureInfo.InvariantCulture))
            .Instant("issuedAt", DocumentWording.Labels.IssuedAt, stamp.IssuedAt, calendar)
            .Text("cause", DocumentWording.Labels.IssuedBecause, DocumentWording.Cause(cause));
        if (coversUntil is { } until)
            section.Instant("coversUntil", DocumentWording.Labels.CoversUntil, until, calendar);
        if (stamp.Previous is { } previous)
        {
            section.Plain(
                stamp.IsCorrection ? "corrects" : "previousVersion",
                stamp.IsCorrection ? DocumentWording.Labels.Corrects : DocumentWording.Labels.PreviousVersion,
                previous.Number);
        }
    }

    private static void PartiesSection(DocumentContent content, DocumentIssuer issuer, DocumentParties parties)
    {
        var section = content.Section("parties", DocumentWording.Headings.Parties)
            .Text("issuedBy", DocumentWording.Labels.IssuedBy, issuer.LegalName)
            .Plain("issuerRegistration", DocumentWording.Labels.CommercialRegistration, issuer.CommercialRegistration)
            .Text("issuerAddress", DocumentWording.Labels.Address, issuer.Address)
            .Plain("supportEmail", DocumentWording.Labels.SupportEmail, issuer.SupportEmail)
            .Plain("supportPhone", DocumentWording.Labels.SupportPhone, issuer.SupportPhone)
            .Plain("customer", DocumentWording.Labels.Customer, parties.Customer.Name)
            .Plain("office", DocumentWording.Labels.Office, parties.Office.Name)
            .Plain("officeRegistration", DocumentWording.Labels.OfficeRegistration, parties.Office.CommercialRegistration);
        if (DocumentWording.Location(parties.Office) is { } location)
            section.Text("officeLocation", DocumentWording.Labels.OfficeLocation, location);
    }

    private void BookingSection(DocumentContent content, Booking booking, DocumentParties parties)
    {
        var section = content.Section("booking", DocumentWording.Headings.Booking)
            .Plain("reference", DocumentWording.Labels.Reference, booking.Reference.Value)
            .Text("status", DocumentWording.Labels.BookingStatus, DocumentWording.BookingStatus(booking.Status))
            .Plain("car", DocumentWording.Labels.Car, DocumentWording.Car(parties.Vehicle));
        if (parties.Vehicle.CarType is { } carType)
            section.Text("carType", DocumentWording.Labels.CarType, carType);
        section.Plain("plate", DocumentWording.Labels.Plate, parties.Vehicle.Plate)
            .Instant("rentalStart", DocumentWording.Labels.RentalStart, booking.Period.Start, calendar)
            .Instant("rentalEnd", DocumentWording.Labels.RentalEnd, booking.Period.End, calendar)
            .Text("days", DocumentWording.Labels.RentalDays, DocumentWording.Days(booking.Pricing.Days))
            .Text("pickupMethod", DocumentWording.Labels.PickupMethod, DocumentWording.PickupMethod(booking.PickupMethod));
    }

    // ── The statement ──────────────────────────────────────────────────────────────────────────────

    private static JsonObject SummaryFacts(FinancialSummaryDto summary, Money net) =>
        new()
        {
            ["days"] = summary.Days,
            ["dailyRate"] = MoneyOf(summary.DailyRate),
            ["depositPercent"] = summary.DepositPercent.ToString("0.##", CultureInfo.InvariantCulture),
            ["rentalSubtotal"] = MoneyOf(summary.RentalSubtotal),
            ["deliveryFee"] = MoneyOf(summary.DeliveryFee),
            ["bookingTotal"] = MoneyOf(summary.BookingTotal),
            ["requiredDeposit"] = MoneyOf(summary.RequiredDeposit),
            ["securityDeposit"] = MoneyOf(summary.SecurityDeposit),
            ["paidOnline"] = MoneyOf(summary.PaidOnline),
            ["processingFees"] = MoneyOf(summary.ProcessingFees!),
            ["chargedOnline"] = MoneyOf(summary.ChargedOnline!),
            ["refunded"] = MoneyOf(summary.Refunded),
            ["refundInProgress"] = MoneyOf(summary.RefundInProgress),
            ["refundDelayed"] = MoneyOf(summary.RefundDelayed),
            ["netPaidOnline"] = SnapshotJson.Money(net),
        };

    private JsonObject BalanceFacts(FinancialBalanceDto balance) =>
        new()
        {
            ["state"] = balance.State,
            ["amount"] = MoneyOf(balance.Amount),
            ["cashRecorded"] = new JsonArray(
            [
                .. balance.CashRecorded.Select(cash => (JsonNode)new JsonObject
                {
                    ["handover"] = cash.Handover,
                    ["amount"] = MoneyOf(cash.Amount),
                    ["recordedAt"] = Instant(cash.RecordedAt),
                }),
            ]),
        };

    private JsonObject DepositFacts(FinancialDepositDto deposit) =>
        new()
        {
            ["state"] = deposit.State,
            ["amount"] = MoneyOf(deposit.Amount),
            ["windowEndsAt"] = SnapshotJson.OptionalInstant(deposit.WindowEndsAt, calendar),
            ["refund"] = deposit.Refund is null ? null : RefundFacts(deposit.Refund),
            ["decision"] = deposit.Decision is null
                ? null
                : new JsonObject
                {
                    ["decidedAt"] = Instant(deposit.Decision.DecidedAt),
                    // The customer's own share only (decision 3): the office's, the platform's and any
                    // charge to the office are null in this projection and are never written.
                    ["toCustomer"] = MoneyOf(deposit.Decision.ToCustomer!),
                    ["refundStatus"] = deposit.Decision.ToCustomerRefundStatus,
                },
        };

    private JsonNode PaymentFacts(FinancialPaymentDto payment) =>
        new JsonObject
        {
            ["paymentId"] = payment.PaymentId.ToString("D", CultureInfo.InvariantCulture),
            ["purpose"] = payment.Purpose,
            ["status"] = payment.Status,
            ["occurredAt"] = Instant(payment.OccurredAt),
            ["amountCharged"] = MoneyOf(payment.AmountCharged!),
            ["processingFee"] = MoneyOf(payment.ProcessingFee!),
            ["feeRefundable"] = payment.FeeRefundable!.Value,
            ["appliedToBooking"] = MoneyOf(payment.AppliedToBooking),
            ["refunds"] = new JsonArray([.. payment.Refunds.Select(refund => (JsonNode)RefundFacts(refund))]),
        };

    private JsonObject RefundFacts(FinancialRefundDto refund) =>
        new()
        {
            ["refundId"] = refund.RefundId.ToString("D", CultureInfo.InvariantCulture),
            ["reason"] = refund.Reason,
            ["status"] = refund.Status,
            ["amount"] = MoneyOf(refund.Amount),
            ["bookingPart"] = MoneyOf(refund.BookingPart),
            ["feePart"] = MoneyOf(refund.FeePart!),
            ["settledAt"] = SnapshotJson.OptionalInstant(refund.SettledAt, calendar),
        };

    private static void ChargesSection(DocumentContent content, FinancialSummaryDto summary)
    {
        var section = content.Section("charges", DocumentWording.Headings.Charges)
            .Money("rental", DocumentWording.Rental(summary.Days, ToMoney(summary.DailyRate)), ToMoney(summary.RentalSubtotal));
        if (summary.DeliveryFee.Amount != 0m)
            section.Money("deliveryFee", DocumentWording.Labels.DeliveryFee, ToMoney(summary.DeliveryFee));
        section.Money("bookingTotal", DocumentWording.Labels.BookingTotal, ToMoney(summary.BookingTotal))
            .Money("requiredDeposit", DocumentWording.RequiredDepositPercent(summary.DepositPercent), ToMoney(summary.RequiredDeposit));
        if (summary.SecurityDeposit.Amount != 0m)
            section.Money("securityDeposit", DocumentWording.Labels.SecurityDeposit, ToMoney(summary.SecurityDeposit));
    }

    private void PaymentSection(DocumentContent content, FinancialPaymentDto payment, int index)
    {
        var applied = payment.Status == PaymentStatus.Applied.Name;
        var heading = applied
            ? DocumentWording.Purpose(Enumeration.FromName<PaymentPurpose>(payment.Purpose))
            : DocumentWording.NotApplied;
        var section = content.Section(string.Create(CultureInfo.InvariantCulture, $"payment{index}"), heading)
            .Instant("paidOn", DocumentWording.Labels.PaidOn, payment.OccurredAt, calendar)
            .Money("amountPaid", DocumentWording.Labels.AmountPaid, ToMoney(payment.AmountCharged!));
        if (payment.ProcessingFee!.Amount != 0m)
        {
            section.Money("processingFee", DocumentWording.Labels.ProcessingFee, ToMoney(payment.ProcessingFee))
                .Text("feeRefundable", DocumentWording.Labels.FeeRefundability, DocumentWording.FeeRefundable(payment.FeeRefundable!.Value));
        }

        if (applied)
            section.Money("appliedToBooking", DocumentWording.Labels.AppliedToBooking, ToMoney(payment.AppliedToBooking));
        else
            section.Text("notApplied", null, DocumentWording.OrphanNote);
    }

    private void RefundsSection(DocumentContent content, IReadOnlyList<FinancialPaymentDto> payments)
    {
        var index = 0;
        foreach (var refund in payments.SelectMany(payment => payment.Refunds))
        {
            var reason = DocumentWording.RefundReason(Enumeration.FromName<RefundReason>(refund.Reason));
            var status = Enumeration.FromName<RefundStatus>(refund.Status);
            var section = content.Section(string.Create(CultureInfo.InvariantCulture, $"refund{++index}"), BilingualText.Of(
                    $"{DocumentWording.Headings.Refund.En}: {reason.En}",
                    $"{DocumentWording.Headings.Refund.Ar}: {reason.Ar}"))
                .Money("amountRefunded", DocumentWording.Labels.AmountRefunded, ToMoney(refund.Amount));
            if (refund.FeePart!.Amount != 0m)
            {
                section.Money("bookingPart", DocumentWording.Labels.BookingMoneyInRefund, ToMoney(refund.BookingPart))
                    .Money("feePart", DocumentWording.Labels.FeeInRefund, ToMoney(refund.FeePart));
            }

            section.Text("status", DocumentWording.Labels.RefundStatus, DocumentWording.RefundStatus(status));
            if (refund.SettledAt is { } settled)
                section.Instant("refundedAt", DocumentWording.Labels.RefundedAt, settled, calendar);
        }
    }

    private void DepositSection(DocumentContent content, FinancialDepositDto deposit)
    {
        var windowEnds = deposit.WindowEndsAt is { } end ? SnapshotJson.Local(end, calendar) : null;
        var share = deposit.Decision?.ToCustomer is { } toCustomer ? ToMoney(toCustomer) : null;
        var section = content.Section("deposit", DocumentWording.Headings.Deposit)
            .Text("state", null, DocumentWording.Deposit(deposit.State, ToMoney(deposit.Amount), windowEnds, share));
        if (deposit.Decision?.ToCustomerRefundStatus is { } refundStatus)
        {
            section.Text(
                "shareRefundStatus",
                DocumentWording.Labels.RefundStatus,
                DocumentWording.RefundStatus(Enumeration.FromName<RefundStatus>(refundStatus)));
        }
    }

    private static void BalanceSection(DocumentContent content, FinancialBalanceDto balance)
    {
        var section = content.Section("balance", DocumentWording.Headings.Balance);
        switch (balance.State)
        {
            case BalanceStates.PaidInFull:
                section.Text("state", null, DocumentWording.PaidInFull);
                break;
            case BalanceStates.NotDue:
                section.Text("state", null, DocumentWording.NothingFurtherDue);
                break;
            case BalanceStates.NotYetDue:
                section.Text("state", null, DocumentWording.NothingDueYet);
                break;
            case BalanceStates.DueAtHandover:
                section.Money("dueAtPickup", DocumentWording.Labels.BalanceDueAtPickup, ToMoney(balance.Amount));
                break;
            case BalanceStates.CashAtHandover:
                section.Money("wasDueAtPickup", DocumentWording.Labels.BalanceWasDueAtPickup, ToMoney(balance.Amount));
                var index = 0;
                foreach (var cash in balance.CashRecorded)
                {
                    section.Money(
                        string.Create(CultureInfo.InvariantCulture, $"cash{++index}"),
                        cash.Handover == HandoverType.Return.Name ? DocumentWording.Labels.CashAtReturn : DocumentWording.Labels.CashAtPickup,
                        ToMoney(cash.Amount));
                }

                break;
            default:
                throw new InvalidOperationException($"A balance state with no words: {balance.State}.");
        }
    }

    private static void TotalsSection(DocumentContent content, FinancialSummaryDto summary, Money net)
    {
        var section = content.Section("totals", DocumentWording.Headings.Totals)
            .Money("chargedOnline", DocumentWording.Labels.ChargedOnline, ToMoney(summary.ChargedOnline!))
            .Money("refunded", DocumentWording.Labels.RefundedToYou, ToMoney(summary.Refunded));
        if (summary.RefundInProgress.Amount != 0m)
            section.Money("refundInProgress", DocumentWording.Labels.RefundInProgress, ToMoney(summary.RefundInProgress));
        if (summary.RefundDelayed.Amount != 0m)
            section.Money("refundDelayed", DocumentWording.Labels.RefundDelayed, ToMoney(summary.RefundDelayed));
        section.Money("netPaidOnline", DocumentWording.Labels.NetPaidOnline, net);
    }

    private static void DocumentsSection(DocumentContent content, IReadOnlyList<ReceiptReference> receipts, DocumentStamp stamp)
    {
        if (receipts.Count == 0 && stamp.Previous is null)
            return;

        var section = content.Section("documents", DocumentWording.Headings.Documents);
        var index = 0;
        foreach (var receipt in receipts)
        {
            section.Plain(
                string.Create(CultureInfo.InvariantCulture, $"receipt{++index}"),
                DocumentWording.Title(receipt.Type),
                receipt.Number);
        }

        if (stamp.Previous is { } previous && !stamp.IsCorrection)
            section.Plain("previousStatement", DocumentWording.Labels.PreviousStatement, previous.Number);
    }

    // ── Small helpers ──────────────────────────────────────────────────────────────────────────────

    private JsonObject Instant(DateTimeOffset at) => SnapshotJson.Instant(at, calendar);

    private static JsonObject MoneyOf(MoneyDto money) => SnapshotJson.Money(ToMoney(money));

    private static Money ToMoney(MoneyDto money) => Money.Create(money.Amount, money.Currency);

    /// <summary>(instant, id) at or before (instant, id): the one total order both "to date" rules use.</summary>
    private static bool AtOrBefore(DateTimeOffset at, Id id, DateTimeOffset thisAt, Id thisId) =>
        at < thisAt || (at == thisAt && id.Value.CompareTo(thisId.Value) <= 0);

    private sealed record Position(Money PaidToDate, Money BalanceAfter, string State);
}

internal static class DocumentSectionExtensions
{
    /// <summary>Adds a line chosen by the caller, keeping a section's lines in one readable chain.</summary>
    public static DocumentSection Add(this DocumentSection section, Func<DocumentSection, DocumentSection> line) => line(section);
}
