using CSharpFunctionalExtensions;
using Khadra.Application.AdminDashboard.Dtos;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using MediatR;

namespace Khadra.Application.AdminDashboard.GetBookingCounts;

/// <summary>
/// The booking KPI card, including how many were taken today.
///
/// "Today" is a LOCAL calendar day (AdminDashboard:ReportingTimeZone, Asia/Amman), never a UTC one:
/// a booking taken at 01:00 in Amman belongs to that day and not the one before, so bucketing in UTC
/// would misreport the figure every night. The trend endpoint buckets against the same calendar, so
/// this card and the last bar of that chart still agree — not because they share a query any more,
/// but because they share the one definition of which day it is.
/// </summary>
public sealed record GetBookingCountsQuery : IQuery<Result<BookingCountsDto, Error>>;

public sealed class GetBookingCountsHandler(
    IBookingDashboardReader bookings,
    IReportingCalendar calendar,
    IClock clock)
    : IRequestHandler<GetBookingCountsQuery, Result<BookingCountsDto, Error>>
{
    public async Task<Result<BookingCountsDto, Error>> Handle(
        GetBookingCountsQuery request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var startOfToday = calendar.StartOfDay(calendar.Today(now));
        var counts = await bookings.CountsAsync(startOfToday, cancellationToken);

        return new BookingCountsDto(now, counts.Total, counts.Today, counts.Active, counts.PendingApproval);
    }
}
