using System.ComponentModel.DataAnnotations;
using Khadra.Application.Common.Ports;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Configuration;

// Shaping numbers for the admin dashboard.
//
// Kept out of BusinessRules on purpose. Those values are frozen onto every booking as BookingTerms
// and judged against for that booking's whole life; "how full an SLA bar must be before it turns
// amber" is a display decision, and freezing it onto financial records would be nonsense.
public sealed class AdminDashboardOptions
{
    public const string SectionName = "AdminDashboard";

    /// <summary>Fraction of the SLA window elapsed before an item is flagged as approaching its deadline.</summary>
    [Range(0.05, 1.0)]
    public decimal SlaWarningThreshold { get; init; } = 0.75m;

    /// <summary>Days covered by the booking trend chart. The same length again is read for the comparison.</summary>
    [Range(1, 90)]
    public int TrendDays { get; init; } = 14;

    [Range(1, 50)]
    public int ActivityFeedSize { get; init; } = 7;

    [Range(1, 365)]
    public int ResolvedDisputeWindowDays { get; init; } = 30;

    /// <summary>
    /// The calendar an admin reads the figures in. "Today" and the daily buckets are local questions:
    /// a booking taken at 01:00 in Amman belongs to that day, not to the previous UTC one.
    /// </summary>
    [Required]
    public string ReportingTimeZone { get; init; } = "Asia/Amman";
}

internal sealed class AdminDashboardSettings(IOptions<AdminDashboardOptions> options) : IAdminDashboardSettings
{
    public decimal SlaWarningThreshold => options.Value.SlaWarningThreshold;

    public int TrendDays => options.Value.TrendDays;

    public int ActivityFeedSize => options.Value.ActivityFeedSize;

    public int ResolvedDisputeWindowDays => options.Value.ResolvedDisputeWindowDays;
}
