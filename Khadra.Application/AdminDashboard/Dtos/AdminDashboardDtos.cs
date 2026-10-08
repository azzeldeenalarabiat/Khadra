using Khadra.Application.Common.Dtos;

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
///
/// <see cref="SlaDeadlineAt"/> is null for a row with no clock (payments Phase 4b): money owed back has
/// no deadline anybody has frozen, and inventing one would be a business number nobody decided. Such a
/// row still carries <see cref="SlaStartedAt"/> — how long it has waited.
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
    DateTimeOffset? SlaDeadlineAt,
    bool IsOverdue);

/// <summary>The audit feed as the dashboard shows it: newest first, capped by AdminDashboard:ActivityFeedSize.</summary>
public sealed record ActivityFeedDto(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ActivityEntryDto> Entries);

/// <param name="ActorUserId">Null when nobody acted, and the client words the actor; see <c>ActivityEntry</c>.</param>
/// <param name="EntityId">The record acted on.</param>
/// <param name="BookingReference">The booking the entry is about, for Booking and Dispute entries; see <c>ActivityEntry</c>.</param>
public sealed record ActivityEntryDto(
    Guid Id,
    DateTimeOffset OccurredAt,
    Guid? ActorUserId,
    string ActorName,
    string Action,
    string EntityType,
    string SubjectLabel,
    Guid? EntityId,
    string? BookingReference);

/// <summary>
/// "Money in motion" (payments Phase 4b): what moved through the platform this Amman month, and what is
/// still owed back right now. No commission or revenue figure: commission is earned per booking and
/// decided by its financial state, and a platform-wide sum of it is the office payables ledger's
/// (payments Phase 8).
/// </summary>
/// <param name="PaymentMode">The provider's own answer — None, Sandbox or Live — as <c>/app-config</c> publishes it.</param>
/// <param name="Currency">The platform's currency. Every figure below is in it; others are listed apart.</param>
/// <param name="OtherCurrencies">
/// Money in another currency: only ever a capture the provider took in the wrong one, which is listed
/// here and never added to a figure in the platform's.
/// </param>
public sealed record FinanceSummaryDto(
    DateTimeOffset GeneratedAt,
    string PaymentMode,
    string Currency,
    FinanceThisMonthDto ThisMonth,
    FinanceRightNowDto RightNow,
    IReadOnlyList<FinanceOtherCurrencyDto> OtherCurrencies);

/// <summary>The month's FLOWS, each counted by its own event date inside [From, To).</summary>
/// <param name="From">The month's first Amman day.</param>
/// <param name="To">The next month's first Amman day: exclusive.</param>
/// <param name="AppliedToBookings">Booking money from payments that applied this month, fees excluded.</param>
/// <param name="ProcessingFeesCharged">
/// The processing fees charged on those payments. Charged, not kept: a refundable fee goes back inside
/// a refund.
/// </param>
/// <param name="RefundsSettled">Refunds that reached the customer this month, whatever month their payment was in.</param>
public sealed record FinanceThisMonthDto(
    DateOnly From,
    DateOnly To,
    MoneyDto AppliedToBookings,
    int PaymentsApplied,
    MoneyDto ProcessingFeesCharged,
    MoneyDto RefundsSettled,
    int RefundsSettledCount);

/// <summary>The STOCK of refunds owed back at <c>GeneratedAt</c>: the same refunds the refunds queue lists.</summary>
/// <param name="RefundsInProgress">Recorded or sent, not back yet.</param>
/// <param name="RefundsFailed">Refused by the provider, still owed, and being sent again.</param>
/// <param name="OrphanedCapturesOwed">
/// Of the two above, what goes back from captures that could not be applied: a PART of them, never added to them.
/// </param>
/// <param name="OpenCaptureIncidentsCount">
/// Capture incidents nobody has marked handled (Wave 4, B1): a second charge, or a capture the provider reported in a
/// way no booking accounts for. A count, not money: what the money is, a person decides at the provider.
/// </param>
public sealed record FinanceRightNowDto(
    MoneyDto RefundsInProgress,
    int RefundsInProgressCount,
    MoneyDto RefundsFailed,
    int RefundsFailedCount,
    MoneyDto OrphanedCapturesOwed,
    int OrphanedCapturesOwedCount,
    int OpenCaptureIncidentsCount = 0);

/// <summary>Money in a currency that is not the platform's, listed apart rather than summed.</summary>
public sealed record FinanceOtherCurrencyDto(
    string Currency,
    MoneyDto RefundsSettledThisMonth,
    MoneyDto RefundsOutstanding);
