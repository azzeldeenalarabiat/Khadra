using CSharpFunctionalExtensions;
using Khadra.Application.AdminDashboard.Dtos;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using MediatR;

namespace Khadra.Application.AdminDashboard.GetBookingTrend;

/// <summary>
/// Daily bookings over the reporting window, and how that compares with the window before it.
///
/// Bucketed against the LOCAL reporting calendar rather than UTC — the same calendar the booking KPI
/// counts "today" with, which is what keeps the card and the last bar of this chart agreeing now that
/// they are two requests.
/// </summary>
public sealed record GetBookingTrendQuery : IQuery<Result<BookingTrendDto, Error>>;

public sealed class GetBookingTrendHandler(
    IBookingDashboardReader bookings,
    IReportingCalendar calendar,
    IAdminDashboardSettings settings,
    IClock clock)
    : IRequestHandler<GetBookingTrendQuery, Result<BookingTrendDto, Error>>
{
    public async Task<Result<BookingTrendDto, Error>> Handle(
        GetBookingTrendQuery request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var today = calendar.Today(now);

        // Two windows are read, not one: the chart's own days plus the same span before it, because
        // the percentage underneath the chart compares them. EarliestDayNeeded owns that arithmetic.
        var earliest = BookingTrendBuilder.EarliestDayNeeded(today, settings.TrendDays);
        var instants = await bookings.CreatedBetweenAsync(
            calendar.StartOfDay(earliest),
            calendar.StartOfDay(today.AddDays(1)),
            cancellationToken);

        return BookingTrendBuilder.Build(instants, calendar, today, settings.TrendDays, now);
    }
}
