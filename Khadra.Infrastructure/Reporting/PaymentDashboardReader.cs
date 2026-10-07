using Khadra.Application.Payments.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.Payments;
using Khadra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Reporting;

/// <summary>
/// The dashboard's reading of the Payments context (payments Phase 4b): the facts the finance panel is
/// summed from, and the refunds and captures a human has to look at. Facts, not totals: the panel adds
/// them up in <c>FinanceSummaryBuilder</c>, and the month's applied payments are loaded as PAYMENTS so
/// their booking money and fee are the payment's own arithmetic, never a second spelling in SQL.
/// </summary>
internal sealed class PaymentDashboardReader(KhadraDbContext context) : IPaymentDashboardReader
{
    public async Task<FinanceFacts> FinanceAsync(
        DateTimeOffset monthStart,
        DateTimeOffset monthEnd,
        CancellationToken cancellationToken = default)
    {
        var applied = PaymentStatus.Applied;
        var settled = RefundStatus.Settled;

        // AppliedAt is when the booking took the money — the platform's clock, not the provider's
        // capture time — so a month's figure moves only when a booking was actually paid in it.
        var payments = await context.Payments
            .Where(payment => payment.Status == applied && payment.AppliedAt >= monthStart && payment.AppliedAt < monthEnd)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var settledThisMonth = await context.Set<Refund>()
            .Where(refund => refund.Status == settled && refund.SettledAt >= monthStart && refund.SettledAt < monthEnd)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var outstanding = await context.Set<Refund>()
            .Where(refund => refund.Status != settled)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var openIncidents = await context.PaymentIncidents.CountAsync(incident => incident.HandledAt == null, cancellationToken);

        return new FinanceFacts(
            [.. payments.Select(payment => new AppliedPaymentFact(
                payment.AppliedToBooking.Amount,
                payment.ProcessingFee.Amount,
                payment.Amount.CurrencyCode))],
            [.. settledThisMonth.Select(Fact)],
            [.. outstanding.Select(Fact)],
            openIncidents);
    }

    public async Task<IReadOnlyList<OpenCaptureIncidentItem>> OpenCaptureIncidentsAsync(CancellationToken cancellationToken = default)
    {
        var open = await context.PaymentIncidents
            .Where(incident => incident.HandledAt == null)
            .OrderBy(incident => incident.DetectedAt)
            .ThenBy(incident => incident.Id)
            .Select(incident => new { incident.Id, incident.PaymentId, incident.Kind, incident.DetectedAt })
            .ToListAsync(cancellationToken);
        if (open.Count == 0)
            return [];

        var paymentIds = open.Select(incident => incident.PaymentId).Distinct().ToList();
        var bookingOf = await context.Payments
            .Where(payment => paymentIds.Contains(payment.Id))
            .Select(payment => new { payment.Id, payment.BookingId })
            .ToDictionaryAsync(payment => payment.Id, payment => payment.BookingId, cancellationToken);
        var references = await ReferencesAsync([.. bookingOf.Values.Distinct()], cancellationToken);

        return
        [
            .. open.Select(incident => new OpenCaptureIncidentItem(
                incident.Id.Value,
                incident.PaymentId.Value,
                bookingOf.TryGetValue(incident.PaymentId, out var bookingId) ? references.GetValueOrDefault(bookingId) : null,
                incident.Kind.Name,
                incident.DetectedAt)),
        ];
    }

    public async Task<IReadOnlyList<FailedRefundItem>> FailedRefundsAsync(
        int refusedAtLeast,
        CancellationToken cancellationToken = default)
    {
        var failed = RefundStatus.Failed;
        var refunds = await context.Set<Refund>()
            .Where(refund => refund.Status == failed && refund.RefusalCount >= refusedAtLeast)
            .OrderBy(refund => refund.RequestedAt)
            .ThenBy(refund => refund.Id)
            .Select(refund => new { refund.Id, refund.PaymentId, refund.RequestedAt })
            .ToListAsync(cancellationToken);
        if (refunds.Count == 0)
            return [];

        var paymentIds = refunds.Select(refund => refund.PaymentId).Distinct().ToList();
        var bookingOf = await context.Payments
            .Where(payment => paymentIds.Contains(payment.Id))
            .Select(payment => new { payment.Id, payment.BookingId })
            .ToDictionaryAsync(payment => payment.Id, payment => payment.BookingId, cancellationToken);
        var references = await ReferencesAsync([.. bookingOf.Values.Distinct()], cancellationToken);

        return
        [
            .. refunds
                .Where(refund => bookingOf.ContainsKey(refund.PaymentId))
                .Select(refund =>
                {
                    var bookingId = bookingOf[refund.PaymentId];
                    return new FailedRefundItem(
                        refund.Id.Value,
                        bookingId.Value,
                        references.GetValueOrDefault(bookingId),
                        refund.RequestedAt);
                }),
        ];
    }

    public async Task<IReadOnlyList<OwedOrphanItem>> OwedOrphansAsync(CancellationToken cancellationToken = default)
    {
        var orphaned = PaymentStatus.Orphaned;
        var orphanedCapture = RefundReason.OrphanedCapture;
        var requested = RefundStatus.Requested;
        var sent = RefundStatus.Sent;

        // Recorded or sent only: a REFUSED orphan refund is already a failed refund, and listing it here
        // too would count one refund twice.
        var orphans = await context.Payments
            .Where(payment =>
                payment.Status == orphaned &&
                context.Set<Refund>().Any(refund =>
                    refund.PaymentId == payment.Id &&
                    refund.Reason == orphanedCapture &&
                    (refund.Status == requested || refund.Status == sent)))
            .OrderBy(payment => payment.OrphanedAt)
            .ThenBy(payment => payment.Id)
            .Select(payment => new { payment.Id, payment.BookingId, payment.OrphanedAt, payment.CreatedAt })
            .ToListAsync(cancellationToken);
        if (orphans.Count == 0)
            return [];

        var references = await ReferencesAsync([.. orphans.Select(orphan => orphan.BookingId).Distinct()], cancellationToken);
        return
        [
            .. orphans.Select(orphan => new OwedOrphanItem(
                orphan.Id.Value,
                orphan.BookingId.Value,
                references.GetValueOrDefault(orphan.BookingId),
                orphan.OrphanedAt ?? orphan.CreatedAt)),
        ];
    }

    private static RefundFact Fact(Refund refund) =>
        new(refund.Amount.Amount, refund.Amount.CurrencyCode, refund.Status.Name, refund.Reason.Name);

    private async Task<Dictionary<Id, string>> ReferencesAsync(List<Id> bookingIds, CancellationToken cancellationToken) =>
        await context.Bookings
            .Where(booking => bookingIds.Contains(booking.Id))
            .Select(booking => new { booking.Id, Reference = booking.Reference.Value })
            .ToDictionaryAsync(booking => booking.Id, booking => booking.Reference, cancellationToken);
}
