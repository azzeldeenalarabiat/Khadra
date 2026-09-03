using System.ComponentModel.DataAnnotations;
using Khadra.Application.Common.Ports;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Configuration;

public sealed class DealerConsoleOptions
{
    public const string SectionName = "DealerConsole";

    [Range(1, 168)]
    public int UpcomingWindowHours { get; init; } = 48;

    public DayOfWeek ReportingWeekStart { get; init; } = DayOfWeek.Sunday;
}

internal sealed class DealerConsoleSettings(IOptions<DealerConsoleOptions> options) : IDealerConsoleSettings
{
    public TimeSpan UpcomingWindow => TimeSpan.FromHours(options.Value.UpcomingWindowHours);

    public DayOfWeek ReportingWeekStart => options.Value.ReportingWeekStart;
}
