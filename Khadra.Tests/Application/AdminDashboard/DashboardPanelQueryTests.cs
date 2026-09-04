using Khadra.Application.AdminDashboard.GetAdminWorkload;
using Khadra.Application.AdminDashboard.GetBookingCounts;
using Khadra.Application.AdminDashboard.GetDisputeCounts;
using Khadra.Application.Auditing.ReadModels;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common.Ports;
using Khadra.Application.Dealers.ReadModels;
using Khadra.Application.Disputes.ReadModels;
using Khadra.Domain.Common;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.AdminDashboard;

// The dashboard used to be one composite handler with no tests at all. Splitting it into a query per
// panel is the moment to pin the two things the composite decided implicitly and a reader cannot:
// which window "today" means, and what the platform actually owes an admin.
public sealed class DashboardPanelQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 21, 30, 0, TimeSpan.Zero);

    private static readonly IReportingCalendar Amman = TestBusinessRules.Calendar();

    private static IAdminDashboardSettings Settings(int resolvedWindowDays = 30)
    {
        var settings = Substitute.For<IAdminDashboardSettings>();
        settings.ResolvedDisputeWindowDays.Returns(resolvedWindowDays);
        settings.ActivityFeedSize.Returns(7);
        settings.TrendDays.Returns(14);
        settings.SlaWarningThreshold.Returns(0.75m);
        return settings;
    }

    /// <summary>
    /// "Today" is the local reporting day, and 21:30 UTC is already tomorrow in Amman (UTC+3).
    ///
    /// This is the guarantee that had to survive the split. The KPI used to take its figure from the
    /// trend, so the card and the last bar of the chart could not disagree; they are separate requests
    /// now and agree because both ask <see cref="IReportingCalendar"/> which day it is. A handler that
    /// quietly used UTC midnight would report the wrong number for three hours every night.
    /// </summary>
    [Fact]
    public async Task Bookings_today_is_counted_from_local_midnight_not_utc_midnight()
    {
        var bookings = Substitute.For<IBookingDashboardReader>();
        bookings.CountsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new BookingCounts(402, 3, 153, 55));

        var handler = new GetBookingCountsHandler(bookings, Amman, new TestClock(Now));

        var result = await handler.Handle(new GetBookingCountsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Today);

        // 2026-09-04 00:00 in Amman is 2026-09-03 21:00 UTC -- half an hour BEFORE "now", which is
        // exactly the case a UTC-based window would get wrong.
        await bookings.Received(1).CountsAsync(
            new DateTimeOffset(2026, 9, 3, 21, 0, 0, TimeSpan.Zero),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_resolved_window_travels_with_the_figure_it_describes()
    {
        var disputes = Substitute.For<IDisputeDashboardReader>();
        disputes.CountsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new DisputeCounts(4, 2, 2, 9));

        var handler = new GetDisputeCountsHandler(disputes, Settings(resolvedWindowDays: 14), new TestClock(Now));

        var result = await handler.Handle(new GetDisputeCountsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(9, result.Value.ResolvedRecently);
        // "9 resolved" is meaningless without "in 14 days", so the console is never left to remember
        // which 14 -- and the window it was measured over is the one that is reported.
        Assert.Equal(14, result.Value.ResolvedWindowDays);
        await disputes.Received(1).CountsAsync(
            Now.AddDays(-14),
            Now,
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The badge counts what the PLATFORM owes, which is PendingReview alone.
    ///
    /// It used to add ClarificationNeeded, so the rail said four applications needed an admin while
    /// the card beside it said two. An application sent back for clarification is waiting on the
    /// dealer to resubmit; badging it sends an admin to a queue with nothing in it to do.
    /// </summary>
    [Fact]
    public async Task Workload_counts_only_the_applications_the_platform_owes_a_decision_on()
    {
        var dealers = Substitute.For<IDealerDashboardReader>();
        dealers.CountsAsync(Arg.Any<CancellationToken>())
            .Returns(new DealerCounts(Total: 14, Trading: 8, PendingReview: 2, ClarificationNeeded: 2, Rejected: 1, Suspended: 1));

        var disputes = Substitute.For<IDisputeDashboardReader>();
        disputes.CountsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new DisputeCounts(Open: 4, UnderReview: 2, Overdue: 2, ResolvedInWindow: 0));

        var handler = new GetAdminWorkloadHandler(dealers, disputes, new TestClock(Now));

        var result = await handler.Handle(new GetAdminWorkloadQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.DealerApplicationsAwaitingReview);
        Assert.Equal(6, result.Value.LiveDisputes);
        Assert.Equal(Now, result.Value.GeneratedAt);
    }
}
