using System.ComponentModel.DataAnnotations;

namespace Khadra.Infrastructure.Configuration;

/// <summary>
/// How often the platform looks for work its own clocks have already decided.
/// </summary>
/// <remarks>
/// The interval is a trade-off with nothing but freshness on one side: no car is held by a lapsed
/// booking — the availability predicate reads the clock, so the vehicle returns to the market at the
/// deadline itself — so a longer interval only delays the moment both parties are TOLD. A minute is
/// short enough that a customer refreshing their bookings sees the truth, and long enough that the
/// query cost is nothing.
/// </remarks>
public sealed class SchedulingOptions
{
    public const string SectionName = "Scheduling";

    /// <summary>
    /// Whether the settlement pass runs at all.
    /// </summary>
    /// <remarks>
    /// A switch rather than a hard-coded yes because tests and one-off tools host the same
    /// application and must not have a timer mutating bookings underneath them. It defaults to ON:
    /// forgetting to enable it would silently reopen checklist item 4, and a platform whose statuses
    /// quietly stop settling is worse than one that says so.
    /// </remarks>
    public bool SettleBookings { get; init; } = true;

    [Range(5, 3600)]
    public int SettlementIntervalSeconds { get; init; } = 60;
}
