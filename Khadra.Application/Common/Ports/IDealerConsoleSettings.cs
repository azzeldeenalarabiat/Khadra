namespace Khadra.Application.Common.Ports;

/// <summary>
/// Numbers the dealer console reads but the business does not set: how far ahead "upcoming" looks,
/// and which day a reporting week starts on. Configuration, never constants (CLAUDE.md).
/// </summary>
public interface IDealerConsoleSettings
{
    /// <summary>The window "upcoming pickups" and "upcoming returns" look ahead over.</summary>
    TimeSpan UpcomingWindow { get; }

    /// <summary>Jordan's working week starts on Sunday; the first non-Jordan market is a settings change.</summary>
    DayOfWeek ReportingWeekStart { get; }
}
