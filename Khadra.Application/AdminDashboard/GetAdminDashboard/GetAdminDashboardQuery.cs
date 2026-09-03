using CSharpFunctionalExtensions;
using Khadra.Application.AdminDashboard.Dtos;
using Khadra.Application.Auditing.ReadModels;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Dealers.ReadModels;
using Khadra.Application.Disputes.ReadModels;
using Khadra.Application.IdentityAccess.ReadModels;
using Khadra.Domain.Common;
using MediatR;

namespace Khadra.Application.AdminDashboard.GetAdminDashboard;

public sealed record GetAdminDashboardQuery : IQuery<Result<AdminDashboardDto, Error>>;

/// <summary>
/// Composes the whole dashboard from one read-model port per bounded context.
///
/// The readers are called SEQUENTIALLY and deliberately so. They share the request's scoped
/// DbContext, and EF Core throws the moment two operations overlap on one context. These are all
/// COUNT queries plus two small windowed reads, so the round trips are cheap; if that ever stops
/// being true the fix is IDbContextFactory, not Task.WhenAll.
///
/// Composition happens here rather than by sending five nested MediatR queries: the pipeline
/// behaviours would run six times per request, and the port is already the seam that becomes an HTTP
/// client if a context is extracted.
/// </summary>
public sealed class GetAdminDashboardHandler(
    IDealerDashboardReader dealers,
    IBookingDashboardReader bookings,
    ICustomerDashboardReader customers,
    IDisputeDashboardReader disputes,
    IAuditFeedReader auditFeed,
    IBusinessRulesProvider businessRules,
    IReportingCalendar calendar,
    IAdminDashboardSettings settings,
    IClock clock)
    : IRequestHandler<GetAdminDashboardQuery, Result<AdminDashboardDto, Error>>
{
    public async Task<Result<AdminDashboardDto, Error>> Handle(
        GetAdminDashboardQuery request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var rules = await businessRules.GetAsync(cancellationToken);
        var today = calendar.Today(now);

        var dealerCounts = await dealers.CountsAsync(cancellationToken);
        var bookingCounts = await bookings.CountsAsync(cancellationToken);
        var customerCounts = await customers.CountsAsync(cancellationToken);

        var resolvedSince = now.AddDays(-settings.ResolvedDisputeWindowDays);
        var disputeCounts = await disputes.CountsAsync(resolvedSince, now, cancellationToken);

        var trend = await BuildTrendAsync(today, cancellationToken);
        var queue = await BuildQueueAsync(rules.AdminSlaHours, now, cancellationToken);
        var activity = await auditFeed.RecentAsync(settings.ActivityFeedSize, cancellationToken);

        return new AdminDashboardDto(
            GeneratedAt: now,
            AdminSlaHours: rules.AdminSlaHours,
            Dealers: new DealerCountsDto(
                dealerCounts.Total,
                dealerCounts.Trading,
                dealerCounts.PendingReview,
                dealerCounts.ClarificationNeeded,
                dealerCounts.Rejected,
                dealerCounts.Suspended),
            Bookings: new BookingCountsDto(
                bookingCounts.Total,
                CountForToday(trend, today),
                bookingCounts.Active,
                bookingCounts.PendingApproval),
            Customers: new CustomerCountsDto(
                customerCounts.Total,
                customerCounts.Verified,
                customerCounts.PendingVerification,
                customerCounts.Suspended),
            Disputes: new DisputeCountsDto(
                disputeCounts.Open,
                disputeCounts.UnderReview,
                disputeCounts.Overdue,
                disputeCounts.ResolvedInWindow,
                settings.ResolvedDisputeWindowDays),
            // The Payments context is not built. Null means "this deployment cannot answer that";
            // a zero would be a claim that the platform took no money.
            Finance: null,
            MoneyInMotion: null,
            BookingTrend: trend,
            AttentionQueue: queue,
            RecentActivity: [.. activity.Select(entry => new ActivityEntryDto(
                entry.Id.Value,
                entry.OccurredAt,
                entry.ActorName,
                entry.Action,
                entry.EntityType,
                entry.SubjectLabel))]);
    }

    private async Task<BookingTrendDto> BuildTrendAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var earliest = BookingTrendBuilder.EarliestDayNeeded(today, settings.TrendDays);
        var instants = await bookings.CreatedBetweenAsync(
            calendar.StartOfDay(earliest),
            calendar.StartOfDay(today.AddDays(1)),
            cancellationToken);

        return BookingTrendBuilder.Build(instants, calendar, today, settings.TrendDays);
    }

    // "Today" comes from the trend rather than a separate query, so the KPI and the last bar of the
    // chart can never disagree, and both are in the same local calendar.
    private static int CountForToday(BookingTrendDto trend, DateOnly today) =>
        trend.Points.FirstOrDefault(point => point.Date == today)?.Count ?? 0;

    private async Task<AttentionQueueDto> BuildQueueAsync(
        int slaHours,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var live = await disputes.LiveAsync(cancellationToken);
        var pending = await dealers.PendingApplicationsAsync(cancellationToken);

        var subtitles = await ResolveDisputeSubtitlesAsync(live, cancellationToken);

        return AttentionQueueBuilder.Build(
            live,
            subtitles,
            pending,
            settings.SlaWarningThreshold,
            slaHours,
            now);
    }

    /// <summary>
    /// Builds "Aqaba Coast Cars · KH-20411" for each live ticket.
    ///
    /// A ticket knows only its BookingId, and a booking knows only its DealerId: cross-context
    /// references are by id, with no navigation properties. So the labels are resolved by asking each
    /// context in turn through its own port, rather than by joining three tables across three
    /// context boundaries in one query.
    /// </summary>
    private async Task<IReadOnlyDictionary<Id, string>> ResolveDisputeSubtitlesAsync(
        IReadOnlyList<LiveDispute> live,
        CancellationToken cancellationToken)
    {
        if (live.Count == 0)
            return new Dictionary<Id, string>();

        var labels = await bookings.LabelsAsync(
            [.. live.Select(dispute => dispute.BookingId).Distinct()],
            cancellationToken);
        var byBooking = labels.ToDictionary(label => label.BookingId);

        var names = await dealers.NamesAsync(
            [.. labels.Select(label => label.DealerId).Distinct()],
            cancellationToken);
        var byDealer = names.ToDictionary(name => name.DealerId, name => name.BusinessName);

        var subtitles = new Dictionary<Id, string>(live.Count);
        foreach (var dispute in live)
        {
            if (!byBooking.TryGetValue(dispute.BookingId, out var label))
                continue;

            var dealerName = byDealer.GetValueOrDefault(label.DealerId);
            subtitles[dispute.TicketId] = dealerName is null
                ? label.Reference
                : $"{dealerName} · {label.Reference}";
        }

        return subtitles;
    }
}
