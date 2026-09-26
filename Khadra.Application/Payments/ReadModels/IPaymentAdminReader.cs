using Khadra.Application.Common;
using Khadra.Application.Common.Dtos;
using Khadra.Domain.Common;
using Khadra.Domain.Payments;

namespace Khadra.Application.Payments.ReadModels;

/// <summary>
/// Which payments the administrator's list shows (payments Phase 4b). Every field is optional; the
/// dates arrive as instants, converted from Amman calendar days by the handler.
/// </summary>
/// <param name="Reference">A booking reference, matched exactly.</param>
/// <param name="CreatedFrom">Inclusive: the instant the first Amman day asked for begins.</param>
/// <param name="CreatedBefore">Exclusive: the instant the day after the last one asked for begins.</param>
public sealed record AdminPaymentFilter(
    PaymentStatus? Status,
    PaymentPurpose? Purpose,
    Id? DealerId,
    Id? CustomerId,
    string? Reference,
    DateTimeOffset? CreatedFrom,
    DateTimeOffset? CreatedBefore);

/// <summary>One checkout attempt in the administrator's payments list (payments Phase 4b).</summary>
/// <remarks>
/// The same names as <c>FinancialPaymentDto</c> for the same facts, so a row and the payment's own page
/// never spell one fact two ways. <see cref="RefundProgress"/> is the payment's own verdict
/// (<c>Payment.RefundProgress</c>), never a second reading of its refunds. Nothing booking-level is here
/// — no deposit, balance or commission state — because those need the booking and its disputes; the
/// booking's page has them. The live checkout URL is never read.
/// </remarks>
/// <param name="DealerName">Null when the dealership no longer resolves; the console words that.</param>
/// <param name="CustomerName">Null when the customer's account no longer resolves.</param>
/// <param name="AmountCharged">What the card was charged: the capture, or what was asked while nothing was.</param>
/// <param name="AppliedToBooking">What went towards the booking: zero unless the payment applied.</param>
/// <param name="RefundSettled">What has reached the customer back from this payment.</param>
public sealed record AdminPaymentListItem(
    Guid PaymentId,
    Guid BookingId,
    string? BookingReference,
    Guid? DealerId,
    string? DealerName,
    Guid CustomerId,
    string? CustomerName,
    string Purpose,
    string Status,
    string RefundProgress,
    MoneyDto AmountCharged,
    MoneyDto ProcessingFee,
    MoneyDto AppliedToBooking,
    MoneyDto RefundSettled,
    bool IsSandbox,
    string? ProviderReference,
    string? FailureCode,
    string? OrphanReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset OccurredAt);

/// <summary>
/// Which refunds the queue shows (item 157's human surface). A null status is the LIVE queue — refused,
/// then recorded, then sent — which is the view an administrator works; <c>Settled</c> is read newest
/// first on its own.
/// </summary>
public sealed record AdminRefundFilter(
    RefundStatus? Status,
    RefundReason? Reason,
    string? Reference,
    DateTimeOffset? RequestedFrom,
    DateTimeOffset? RequestedBefore);

/// <summary>One refund in the administrator's queue: what is owed back, to whom, and where it is.</summary>
/// <remarks>
/// <see cref="Amount"/> is what is owed. The split into booking money and processing fee is on the
/// payment's own page, where the payment it came from is read.
/// </remarks>
public sealed record AdminRefundListItem(
    Guid RefundId,
    Guid PaymentId,
    Guid BookingId,
    string? BookingReference,
    Guid? DealerId,
    string? DealerName,
    Guid CustomerId,
    string? CustomerName,
    string Reason,
    string Status,
    MoneyDto Amount,
    Guid? DisputeTicketId,
    DateTimeOffset RequestedAt,
    DateTimeOffset? SentAt,
    DateTimeOffset? SettledAt,
    DateTimeOffset? FailedAt,
    string? FailureCode,
    string? ProviderReference,
    bool IsSandbox);

/// <summary>The booking a payment belongs to, and its parties, for the payment's page.</summary>
public sealed record PaymentBookingLink(
    Guid BookingId,
    string Reference,
    string Status,
    Guid DealerId,
    string? DealerName,
    Guid CustomerId,
    string? CustomerName);

/// <summary>One event the provider sent about a payment, as it was received and what became of it.</summary>
/// <param name="TiedBy">
/// "Payment" when the receipt names the payment; "Reference" when it carries only the payment's provider
/// reference — a receipt that arrived before the checkout's reference was saved, and the one an
/// investigation needs.
/// </param>
public sealed record ProviderEventItem(
    Guid ReceiptId,
    string ProviderEventId,
    string Kind,
    string Outcome,
    MoneyDto? Amount,
    DateTimeOffset ReceivedAt,
    string TiedBy);

/// <summary>The administrator's reading of payments and refunds across the platform (payments Phase 4b).</summary>
public interface IPaymentAdminReader
{
    /// <summary>Newest first, the id breaking ties, so no page boundary drops or repeats a payment.</summary>
    Task<PagedResult<AdminPaymentListItem>> ListPaymentsAsync(
        AdminPaymentFilter filter,
        PageRequest page,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The live queue ranks refused, then recorded, then sent, each owed-longest first (by
    /// <c>RequestedAt</c>, which never changes: <c>FailedAt</c> is rewritten on every refusal); settled
    /// ones read newest first.
    /// </summary>
    Task<PagedResult<AdminRefundListItem>> ListRefundsAsync(
        AdminRefundFilter filter,
        PageRequest page,
        CancellationToken cancellationToken = default);

    /// <summary>The booking a payment belongs to, or null when it does not resolve.</summary>
    Task<PaymentBookingLink?> BookingLinkAsync(Id bookingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every event the provider sent about the payment, oldest first: the receipts that name it, and the
    /// ones that carry only its provider reference.
    /// </summary>
    Task<IReadOnlyList<ProviderEventItem>> ProviderEventsAsync(
        Id paymentId,
        string provider,
        string? providerReference,
        CancellationToken cancellationToken = default);
}
