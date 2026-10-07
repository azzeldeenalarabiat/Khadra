namespace Khadra.Application.Payments.ReadModels;

/// <summary>
/// One payment that applied to a booking, as the finance panel counts it: the booking money and the
/// processing fee the PAYMENT computes (<c>AppliedToBooking</c>, <c>ProcessingFee</c>), never a second
/// spelling of them in SQL.
/// </summary>
public sealed record AppliedPaymentFact(decimal AppliedToBooking, decimal ProcessingFee, string Currency);

/// <summary>One refund, as the finance panel counts it.</summary>
public sealed record RefundFact(decimal Amount, string Currency, string Status, string Reason);

/// <summary>
/// What the finance panel is built from (payments Phase 4b): the month's FLOWS — payments applied and
/// refunds settled inside it — and the STOCK of refunds still owed right now.
/// </summary>
/// <param name="AppliedThisMonth">Payments whose <c>AppliedAt</c> falls in the month.</param>
/// <param name="SettledThisMonth">Refunds whose <c>SettledAt</c> falls in the month, whatever month their payment was in.</param>
/// <param name="Outstanding">Refunds not settled yet — recorded, sent, or refused and being sent again.</param>
/// <param name="OpenCaptureIncidents">Capture incidents nobody has marked handled yet (Wave 4, B1): a count, never money.</param>
public sealed record FinanceFacts(
    IReadOnlyList<AppliedPaymentFact> AppliedThisMonth,
    IReadOnlyList<RefundFact> SettledThisMonth,
    IReadOnlyList<RefundFact> Outstanding,
    int OpenCaptureIncidents = 0);

/// <summary>A refund the provider refused, still owed and being sent again: work for a human.</summary>
public sealed record FailedRefundItem(Guid RefundId, Guid BookingId, string? BookingReference, DateTimeOffset RequestedAt);

/// <summary>A capture that could not be applied, whose refund is recorded or sent and not back yet.</summary>
public sealed record OwedOrphanItem(Guid PaymentId, Guid BookingId, string? BookingReference, DateTimeOffset OrphanedAt);

/// <summary>
/// A capture incident nobody has marked handled (Wave 4, B1): money may have moved in a way no booking accounts for.
/// </summary>
public sealed record OpenCaptureIncidentItem(
    Guid IncidentId,
    Guid PaymentId,
    string? BookingReference,
    string Kind,
    DateTimeOffset DetectedAt);

/// <summary>The dashboard's reading of the Payments context (payments Phase 4b).</summary>
public interface IPaymentDashboardReader
{
    /// <param name="monthStart">Inclusive: the instant the Amman month begins.</param>
    /// <param name="monthEnd">Exclusive: the instant the next Amman month begins.</param>
    Task<FinanceFacts> FinanceAsync(
        DateTimeOffset monthStart,
        DateTimeOffset monthEnd,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every refund that reads refused and has been refused at least <paramref name="refusedAtLeast"/> times, owed
    /// longest first (Wave 4, B4: below the alert the back-off is handling it, and nobody needs to look yet).
    /// </summary>
    Task<IReadOnlyList<FailedRefundItem>> FailedRefundsAsync(int refusedAtLeast, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every orphaned capture whose refund is recorded or sent but not back — never one whose refund was
    /// REFUSED, which is already a failed refund, so no refund is listed twice. Oldest first.
    /// </summary>
    Task<IReadOnlyList<OwedOrphanItem>> OwedOrphansAsync(CancellationToken cancellationToken = default);

    /// <summary>Every capture incident not yet marked handled, oldest first (Wave 4, B1).</summary>
    Task<IReadOnlyList<OpenCaptureIncidentItem>> OpenCaptureIncidentsAsync(CancellationToken cancellationToken = default);
}
