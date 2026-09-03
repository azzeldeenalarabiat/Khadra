namespace Khadra.Application.AdminDashboard.Dtos;

// The admin dashboard as one snapshot.
//
// Two contract decisions worth stating, because both look like omissions:
//
// 1. Nothing here carries a colour, an icon, a route or a rendered string like "13h over". The
//    console owns its design system; the API owns facts. Kind and Severity are enumerable strings the
//    client maps, and every deadline is an absolute instant so a countdown stays live between
//    refreshes instead of freezing at the moment of the fetch.
//
// 2. Finance and MoneyInMotion are nullable and are null today. The Payments context is not built,
//    and a zero on an admin dashboard is a claim, not an absence. Null says "this deployment cannot
//    answer that"; the console renders an explicit unavailable state. Filling them in later adds no
//    field and breaks no client.
public sealed record AdminDashboardDto(
    DateTimeOffset GeneratedAt,
    // Echoed so the console can label the queue without hardcoding "48-hour SLA".
    int AdminSlaHours,
    DealerCountsDto Dealers,
    BookingCountsDto Bookings,
    CustomerCountsDto Customers,
    DisputeCountsDto Disputes,
    FinanceSummaryDto? Finance,
    MoneyInMotionDto? MoneyInMotion,
    BookingTrendDto BookingTrend,
    AttentionQueueDto AttentionQueue,
    IReadOnlyList<ActivityEntryDto> RecentActivity);

/// <summary>Buckets partition the total; see DealerCounts for why they are not the design's labels.</summary>
public sealed record DealerCountsDto(
    int Total,
    int Trading,
    int PendingReview,
    int ClarificationNeeded,
    int Rejected,
    int Suspended);

public sealed record BookingCountsDto(int Total, int Today, int Active, int PendingApproval);

public sealed record CustomerCountsDto(int Total, int Verified, int PendingVerification, int Suspended);

public sealed record DisputeCountsDto(
    int Open,
    int UnderReview,
    int Overdue,
    int ResolvedRecently,
    int ResolvedWindowDays);

/// <summary>Placeholder shape for the Payments slice. Always null until that context exists.</summary>
public sealed record FinanceSummaryDto(
    MoneyDto GrossBookingValue,
    MoneyDto Commission,
    MoneyDto DealerPayouts,
    MoneyDto Refunds,
    // A revenue figure with no period is not a figure anyone can check.
    DateOnly PeriodFrom,
    DateOnly PeriodTo);

public sealed record MoneyInMotionDto(MoneyDto Gross, MoneyDto Commission, MoneyDto DealerPayouts);

/// <summary>Every money value travels with its currency; the console never assumes JOD.</summary>
public sealed record MoneyDto(decimal Amount, string Currency);

public sealed record BookingTrendDto(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<DailyCountDto> Points,
    /// <summary>Change against the immediately preceding window of the same length; null when that window was empty.</summary>
    decimal? ChangePercent);

public sealed record DailyCountDto(DateOnly Date, int Count);

public sealed record AttentionQueueDto(
    int SlaHours,
    int OpenCount,
    int OverdueCount,
    IReadOnlyList<AttentionItemDto> Items);

/// <summary>
/// One row of the work queue.
///
/// Count is 1 for a single subject and N for a grouped row (the design shows pending dealer
/// applications as one line, not one line each). A client that meets an unknown Kind must still
/// render the row generically: that is what lets the Payments kinds be added without a breaking change.
/// </summary>
public sealed record AttentionItemDto(
    string Id,
    string Kind,
    string Severity,
    int Count,
    IReadOnlyList<Guid> SubjectIds,
    string? Subtitle,
    string? Description,
    DateTimeOffset SlaStartedAt,
    DateTimeOffset SlaDeadlineAt,
    bool IsOverdue);

public sealed record ActivityEntryDto(
    Guid Id,
    DateTimeOffset OccurredAt,
    string ActorName,
    string Action,
    string EntityType,
    string SubjectLabel);
