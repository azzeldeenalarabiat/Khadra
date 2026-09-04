namespace Khadra.Application.AdminDashboard.Dtos;

// The admin dashboard, as one response per panel.
//
// It used to be a single AdminDashboardDto composing every figure on the screen. That was defended
// as "one screen, one refresh", and for the dashboard screen it was fine -- but the sidebar reads
// two of these numbers on EVERY admin screen, so opening the dealer queue paid for the attention
// queue, the fourteen-day trend and the audit feed to render two integers. Measured: 5,305 bytes and
// the whole composition, for 40 bytes of badge.
//
// Splitting also buys what the composite could not have. Its own comment noted that the readers had
// to run SEQUENTIALLY because they share one scoped DbContext; a request per panel gets a scope per
// panel, so the reads that could not overlap inside one handler now genuinely run in parallel. And
// each panel gets its own loading and error state, where before one slow or failing reader blanked
// the entire screen.
//
// Two contract decisions survive from the composite, because both still look like omissions:
//
// 1. Nothing here carries a colour, an icon, a route or a rendered string like "13h over". The
//    console owns its design system; the API owns facts. Kind and Severity are enumerable strings the
//    client maps, and every deadline is an absolute instant so a countdown stays live between
//    refreshes instead of freezing at the moment of the fetch.
//
// 2. The Payments figures are nullable and are null today. That context is not built, and a zero on
//    an admin dashboard is a claim, not an absence. Null says "this deployment cannot answer that";
//    the console renders an explicit unavailable state. Filling them in later adds no field and
//    breaks no client.
//
// Every response carries GeneratedAt. With one snapshot the reader knew when "now" was; with eight
// they each have their own, and a panel that is minutes stale should be able to say so.

/// <summary>Buckets partition the total; see DealerCounts for why they are not the design's labels.</summary>
public sealed record DealerCountsDto(
    DateTimeOffset GeneratedAt,
    int Total,
    int Trading,
    int PendingReview,
    int ClarificationNeeded,
    int Rejected,
    int Suspended);

public sealed record BookingCountsDto(
    DateTimeOffset GeneratedAt,
    int Total,
    int Today,
    int Active,
    int PendingApproval);

public sealed record CustomerCountsDto(
    DateTimeOffset GeneratedAt,
    int Total,
    int Verified,
    int PendingVerification,
    int Suspended);

public sealed record DisputeCountsDto(
    DateTimeOffset GeneratedAt,
    int Open,
    int UnderReview,
    int Overdue,
    int ResolvedRecently,
    int ResolvedWindowDays);

/// <summary>
/// What is sitting with the platform right now. Named for the fact, not for the badge that draws it.
/// </summary>
public sealed record AdminWorkloadDto(
    DateTimeOffset GeneratedAt,
    // Applications where the platform owes a decision -- PendingReview, the domain's own
    // IsAwaitingAdmin. NOT ClarificationNeeded: that ball is with the dealer.
    int DealerApplicationsAwaitingReview,
    // Tickets nobody has closed: Open plus UnderReview.
    int LiveDisputes);

// There is no finance endpoint here, and that is the point of splitting.
//
// The composite carried Finance and MoneyInMotion as permanently-null fields, so every dashboard load
// paid to be told the Payments context does not exist. A screen now calls what can answer it; nothing
// answers for money yet, so nothing is called. The Revenue card and the money panel say so on their
// own, the same way `features/not-built/` speaks for a whole screen. When Payments ships it arrives
// as `GET /admin/dashboard/finance` alongside these, and the console drops its local statement --
// additive, and no shape kept warm in the meantime for a context nobody has written.

public sealed record BookingTrendDto(
    DateTimeOffset GeneratedAt,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<DailyCountDto> Points,
    /// <summary>Change against the immediately preceding window of the same length; null when that window was empty.</summary>
    decimal? ChangePercent);

public sealed record DailyCountDto(DateOnly Date, int Count);

public sealed record AttentionQueueDto(
    DateTimeOffset GeneratedAt,
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

/// <summary>The audit feed as the dashboard shows it: newest first, capped by AdminDashboard:ActivityFeedSize.</summary>
public sealed record ActivityFeedDto(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ActivityEntryDto> Entries);

public sealed record ActivityEntryDto(
    Guid Id,
    DateTimeOffset OccurredAt,
    string ActorName,
    string Action,
    string EntityType,
    string SubjectLabel);
