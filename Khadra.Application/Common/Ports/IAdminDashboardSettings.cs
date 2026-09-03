namespace Khadra.Application.Common.Ports;

// Presentation-shaping numbers for the admin dashboard.
//
// Deliberately NOT part of BusinessRules. Those values are frozen onto every booking as BookingTerms
// and judged against for the life of that booking; "how full must an SLA bar be before we colour it
// amber" has nothing to do with what a customer agreed to, and putting it there would freeze a UI
// tuning knob onto financial records forever.
public interface IAdminDashboardSettings
{
    /// <summary>
    /// How much of the SLA window must have elapsed before an item is called out as approaching its
    /// deadline. Expressed 0-1.
    /// </summary>
    decimal SlaWarningThreshold { get; }

    /// <summary>How many days the booking trend chart covers.</summary>
    int TrendDays { get; }

    /// <summary>How many entries the recent-activity feed shows.</summary>
    int ActivityFeedSize { get; }

    /// <summary>The window used for the "resolved recently" dispute figure.</summary>
    int ResolvedDisputeWindowDays { get; }
}
