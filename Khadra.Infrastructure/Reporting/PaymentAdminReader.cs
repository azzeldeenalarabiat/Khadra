using Khadra.Application.Common;
using Khadra.Application.Common.Dtos;
using Khadra.Application.Payments.ReadModels;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Payments;
using Khadra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Reporting;

/// <summary>
/// The administrator's payments list, refunds queue and payment page (payments Phase 4b). Payments,
/// Bookings, Dealers and Identity are joined BY ID, never navigated. A page of payments is loaded as
/// payments, with their refunds, so every figure on a row — refund progress above all — is the
/// payment's own verdict rather than a second spelling of it here.
/// </summary>
internal sealed class PaymentAdminReader(KhadraDbContext context) : IPaymentAdminReader
{
    public async Task<PagedResult<AdminPaymentListItem>> ListPaymentsAsync(
        AdminPaymentFilter filter,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(page);

        var query = Filtered(filter);
        if (query is null)
            return PagedResult.Empty<AdminPaymentListItem>(page.Page, page.PageSize);

        var total = await query.CountAsync(cancellationToken);
        if (total == 0)
            return PagedResult.Empty<AdminPaymentListItem>(page.Page, page.PageSize);

        var payments = await OrderForList(query)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Include(payment => payment.Refunds)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var labels = await LabelsAsync(
            [.. payments.Select(payment => payment.BookingId).Distinct()],
            [.. payments.Select(payment => payment.CustomerId).Distinct()],
            cancellationToken);

        return new PagedResult<AdminPaymentListItem>(
            [.. payments.Select(payment => Row(payment, labels))],
            page.Page,
            page.PageSize,
            total);
    }

    public async Task<PagedResult<AdminRefundListItem>> ListRefundsAsync(
        AdminRefundFilter filter,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(page);

        var query = FilteredRefunds(filter);
        if (query is null)
            return PagedResult.Empty<AdminRefundListItem>(page.Page, page.PageSize);

        var total = await query.CountAsync(cancellationToken);
        if (total == 0)
            return PagedResult.Empty<AdminRefundListItem>(page.Page, page.PageSize);

        var refunds = await OrderForQueue(query, filter.Status)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var paymentIds = refunds.Select(refund => refund.PaymentId).Distinct().ToList();
        var payments = await context.Payments
            .Where(payment => paymentIds.Contains(payment.Id))
            .Select(payment => new { payment.Id, payment.BookingId, payment.CustomerId, payment.Provider })
            .ToDictionaryAsync(payment => payment.Id, cancellationToken);
        var labels = await LabelsAsync(
            [.. payments.Values.Select(payment => payment.BookingId).Distinct()],
            [.. payments.Values.Select(payment => payment.CustomerId).Distinct()],
            cancellationToken);

        var rows = new List<AdminRefundListItem>(refunds.Count);
        foreach (var refund in refunds)
        {
            // A refund's payment is never deleted; the guard only keeps a broken row from failing the page.
            if (!payments.TryGetValue(refund.PaymentId, out var payment))
                continue;
            var booking = labels.Bookings.GetValueOrDefault(payment.BookingId);
            rows.Add(new AdminRefundListItem(
                refund.Id.Value,
                refund.PaymentId.Value,
                payment.BookingId.Value,
                booking?.Reference,
                booking?.DealerId.Value,
                booking is null ? null : labels.Dealers.GetValueOrDefault(booking.DealerId),
                payment.CustomerId.Value,
                labels.Customers.GetValueOrDefault(payment.CustomerId),
                refund.Reason.Name,
                refund.Status.Name,
                MoneyDto.From(refund.Amount),
                refund.DisputeTicketId?.Value,
                refund.RequestedAt,
                refund.SentAt,
                refund.SettledAt,
                refund.FailedAt,
                refund.FailureCode,
                refund.ProviderReference,
                PaymentProviders.IsSandbox(payment.Provider),
                refund.RefusalCount,
                refund.NextAttemptAt));
        }

        return new PagedResult<AdminRefundListItem>(rows, page.Page, page.PageSize, total);
    }

    public async Task<PaymentBookingLink?> BookingLinkAsync(Id bookingId, CancellationToken cancellationToken = default)
    {
        var row = await context.Bookings
            .Where(booking => booking.Id == bookingId)
            .Select(booking => new
            {
                Reference = booking.Reference.Value,
                booking.Status,
                booking.DealerId,
                booking.CustomerId,
                DealerName = context.Dealers
                    .Where(dealer => dealer.Id == booking.DealerId)
                    .Select(dealer => dealer.BusinessName.Value)
                    .FirstOrDefault(),
                CustomerName = context.Users
                    .Where(user => user.Id == booking.CustomerId)
                    .Select(user => user.Name.Value)
                    .FirstOrDefault(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new PaymentBookingLink(
                bookingId.Value,
                row.Reference,
                row.Status.Name,
                row.DealerId.Value,
                row.DealerName,
                row.CustomerId.Value,
                row.CustomerName);
    }

    public async Task<IReadOnlyList<ProviderEventItem>> ProviderEventsAsync(
        Id paymentId,
        string provider,
        string? providerReference,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        // Tied two ways. A receipt names the payment when the handler found it; one that arrived
        // before the checkout's provider reference was saved was recorded with no payment at all, and
        // carries only OUR reference — the receipt an investigation needs, invisible by payment id.
        Id? tied = paymentId;
        var receipts = await context.ProviderEventReceipts
            .Where(receipt =>
                receipt.PaymentId == tied ||
                (providerReference != null &&
                 receipt.PaymentId == null &&
                 receipt.Provider == provider &&
                 receipt.ProviderReference == providerReference))
            .OrderBy(receipt => receipt.ReceivedAt)
            .ThenBy(receipt => receipt.Id)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return
        [
            .. receipts.Select(receipt => new ProviderEventItem(
                receipt.Id.Value,
                receipt.ProviderEventId,
                receipt.Kind,
                receipt.Outcome.Name,
                receipt.Amount is { } amount && receipt.CurrencyCode is { } currency ? new MoneyDto(amount, currency) : null,
                receipt.ReceivedAt,
                receipt.PaymentId == tied ? "Payment" : "Reference",
                receipt.CaptureReference)),
        ];
    }

    public async Task<IReadOnlyList<PaymentIncidentItem>> IncidentsAsync(Id paymentId, CancellationToken cancellationToken = default)
    {
        var incidents = await context.PaymentIncidents
            .Where(incident => incident.PaymentId == paymentId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        if (incidents.Count == 0)
            return [];

        var adminIds = incidents.Where(incident => incident.HandledByAdminId is not null)
            .Select(incident => incident.HandledByAdminId!.Value)
            .Distinct()
            .ToList();
        var names = adminIds.Count == 0
            ? []
            : await context.Users
                .Where(user => adminIds.Contains(user.Id))
                .Select(user => new { user.Id, Name = user.Name.Value })
                .ToDictionaryAsync(user => user.Id, user => user.Name, cancellationToken);

        return
        [
            .. incidents
                .OrderBy(incident => incident.HandledAt is null ? 0 : 1)
                .ThenBy(incident => incident.DetectedAt)
                .ThenBy(incident => incident.Id.Value)
                .Select(incident => new PaymentIncidentItem(
                    incident.Id.Value,
                    incident.Kind.Name,
                    incident.ReceiptId.Value,
                    incident.CaptureReference,
                    MoneyDto.From(incident.Reported),
                    MoneyDto.From(incident.Expected),
                    incident.OtherPaymentId?.Value,
                    incident.DetectedAt,
                    incident.HandledAt,
                    incident.HandledByAdminId is { } admin ? names.GetValueOrDefault(admin) : null,
                    incident.HandledNote)),
        ];
    }

    /// <summary>The payments this filter names; null for a reference no booking could carry: nothing, not everything.</summary>
    private IQueryable<Payment>? Filtered(AdminPaymentFilter filter)
    {
        var query = context.Payments.AsQueryable();

        if (filter.Status is { } status)
            query = query.Where(payment => payment.Status == status);
        if (filter.Purpose is { } purpose)
            query = query.Where(payment => payment.Purpose == purpose);
        if (filter.CustomerId is { } customerId)
            query = query.Where(payment => payment.CustomerId == customerId);
        if (filter.DealerId is { } dealerId)
            query = query.Where(payment => context.Bookings.Any(booking => booking.Id == payment.BookingId && booking.DealerId == dealerId));
        if (!string.IsNullOrWhiteSpace(filter.Reference))
        {
            // Matched in full through the value-object converter, as the bookings list matches it.
            var reference = BookingReference.Create(filter.Reference.Trim().ToUpperInvariant());
            if (reference.IsFailure)
                return null;
            var wanted = reference.Value;
            query = query.Where(payment => context.Bookings.Any(booking => booking.Id == payment.BookingId && booking.Reference == wanted));
        }
        if (filter.CreatedFrom is { } from)
            query = query.Where(payment => payment.CreatedAt >= from);
        if (filter.CreatedBefore is { } before)
            query = query.Where(payment => payment.CreatedAt < before);

        return query;
    }

    /// <summary>
    /// Newest first, the id breaking the tie: two attempts opened in the same instant share a key, and a
    /// non-total order lets a page boundary drop one from every page. Its own method so a test can read it.
    /// </summary>
    internal static IOrderedQueryable<Payment> OrderForList(IQueryable<Payment> query) =>
        query.OrderByDescending(payment => payment.CreatedAt).ThenByDescending(payment => payment.Id);

    private IQueryable<Refund>? FilteredRefunds(AdminRefundFilter filter)
    {
        var failed = RefundStatus.Failed;
        var requested = RefundStatus.Requested;
        var sent = RefundStatus.Sent;
        var query = context.Set<Refund>().AsQueryable();

        query = filter.Status is { } status
            ? query.Where(refund => refund.Status == status)
            // The live queue: everything still owed.
            : query.Where(refund => refund.Status == failed || refund.Status == requested || refund.Status == sent);

        if (filter.Reason is { } reason)
            query = query.Where(refund => refund.Reason == reason);
        if (!string.IsNullOrWhiteSpace(filter.Reference))
        {
            var reference = BookingReference.Create(filter.Reference.Trim().ToUpperInvariant());
            if (reference.IsFailure)
                return null;
            var wanted = reference.Value;
            query = query.Where(refund => context.Payments.Any(payment =>
                payment.Id == refund.PaymentId &&
                context.Bookings.Any(booking => booking.Id == payment.BookingId && booking.Reference == wanted)));
        }
        if (filter.RequestedFrom is { } from)
            query = query.Where(refund => refund.RequestedAt >= from);
        if (filter.RequestedBefore is { } before)
            query = query.Where(refund => refund.RequestedAt < before);

        return query;
    }

    /// <summary>
    /// The live queue ranks refused, then recorded, then sent, and inside each, owed longest first by
    /// <c>RequestedAt</c> — never <c>FailedAt</c>, which every refusal rewrites, so rows would shuffle
    /// between page loads. Settled refunds read newest first. The id breaks every tie.
    /// </summary>
    internal static IOrderedQueryable<Refund> OrderForQueue(IQueryable<Refund> query, RefundStatus? status)
    {
        if (status == RefundStatus.Settled)
            return query.OrderByDescending(refund => refund.SettledAt).ThenByDescending(refund => refund.Id);

        var failed = RefundStatus.Failed;
        var requested = RefundStatus.Requested;
        return query
            .OrderBy(refund => refund.Status == failed ? 0 : refund.Status == requested ? 1 : 2)
            .ThenBy(refund => refund.RequestedAt)
            .ThenBy(refund => refund.Id);
    }

    private static AdminPaymentListItem Row(Payment payment, Labels labels)
    {
        var booking = labels.Bookings.GetValueOrDefault(payment.BookingId);
        return new AdminPaymentListItem(
            payment.Id.Value,
            payment.BookingId.Value,
            booking?.Reference,
            booking?.DealerId.Value,
            booking is null ? null : labels.Dealers.GetValueOrDefault(booking.DealerId),
            payment.CustomerId.Value,
            labels.Customers.GetValueOrDefault(payment.CustomerId),
            payment.Purpose.Name,
            payment.Status.Name,
            payment.RefundProgress.Name,
            MoneyDto.From(payment.AmountCaptured ?? payment.Amount),
            MoneyDto.From(payment.ProcessingFee),
            MoneyDto.From(payment.Status == PaymentStatus.Applied
                ? payment.AppliedToBooking
                : Money.ZeroIn(payment.Amount.CurrencyCode)),
            MoneyDto.From(payment.RefundSettled),
            payment.IsSandbox,
            payment.ProviderReference,
            payment.FailureCode,
            payment.OrphanReason,
            payment.CreatedAt,
            // The same instant as the payment's own page and its documents: when the money was recorded.
            payment.AppliedAt ?? payment.OrphanedAt ?? payment.CapturedAt ?? payment.FailedAt ?? payment.CreatedAt);
    }

    /// <summary>The bookings, dealerships and customers a page names, each read once.</summary>
    private async Task<Labels> LabelsAsync(
        List<Id> bookingIds,
        List<Id> customerIds,
        CancellationToken cancellationToken)
    {
        var bookings = await context.Bookings
            .Where(booking => bookingIds.Contains(booking.Id))
            .Select(booking => new { booking.Id, Reference = booking.Reference.Value, booking.DealerId })
            .ToListAsync(cancellationToken);
        var dealerIds = bookings.Select(booking => booking.DealerId).Distinct().ToList();
        var dealers = await context.Dealers
            .Where(dealer => dealerIds.Contains(dealer.Id))
            .Select(dealer => new { dealer.Id, Name = dealer.BusinessName.Value })
            .ToDictionaryAsync(dealer => dealer.Id, dealer => dealer.Name, cancellationToken);
        var customers = await context.Users
            .Where(user => customerIds.Contains(user.Id))
            .Select(user => new { user.Id, Name = user.Name.Value })
            .ToDictionaryAsync(user => user.Id, user => user.Name, cancellationToken);

        return new Labels(
            bookings.ToDictionary(booking => booking.Id, booking => new BookingLabel(booking.Reference, booking.DealerId)),
            dealers,
            customers);
    }

    private sealed record BookingLabel(string Reference, Id DealerId);

    private sealed record Labels(
        Dictionary<Id, BookingLabel> Bookings,
        Dictionary<Id, string> Dealers,
        Dictionary<Id, string> Customers);
}
