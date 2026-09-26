using System.Globalization;
using Khadra.Application.AdminDashboard.Dtos;
using Khadra.Application.Dealers.ReadModels;
using Khadra.Application.Disputes.ReadModels;
using Khadra.Application.Payments.ReadModels;
using Khadra.Domain.Common;

namespace Khadra.Application.AdminDashboard;

/// <summary>
/// Turns raw per-context facts into the ordered work queue the dashboard shows.
///
/// Pure and static on purpose. The classification and the ordering are the only genuine decisions on
/// this screen — which items count as overdue, when an item starts warning, and what an admin should
/// look at first — so they live in one function with no I/O that can be unit-tested exhaustively,
/// rather than being spread across a LINQ query and a template.
///
/// It is NOT a domain aggregate. Every fact it works from already belongs to a context that owns it
/// (Dealer.ReviewDueAt, DisputeTicket.SlaDeadline). A WorkItem aggregate would be a mirror of four
/// contexts kept in sync by events, and mirrors drift.
/// </summary>
public static class AttentionQueueBuilder
{
    public static class Kinds
    {
        public const string DisputeOverdue = "DisputeOverdue";
        public const string DisputeOpen = "DisputeOpen";
        public const string DealerApplicationsAtRisk = "DealerApplicationsAtRisk";

        /// <summary>Refunds the provider refused: still owed, being sent again, and a human has to look.</summary>
        public const string RefundFailed = "RefundFailed";

        /// <summary>Captures that could not be applied, whose refund is on its way and not back.</summary>
        public const string OrphanedCaptureOwed = "OrphanedCaptureOwed";

        /// <summary>A deposit held for a penalty against the customer with no dispute (pre-launch item 164).</summary>
        public const string DepositAwaitingDecision = "DepositAwaitingDecision";
    }

    public static class Severities
    {
        public const string Overdue = "Overdue";
        public const string Warning = "Warning";
        public const string Info = "Info";
    }

    /// <param name="slaWarningThreshold">
    /// Fraction of the SLA window (0-1) after which an item is called out as approaching its deadline.
    /// A tuning knob for this screen, not a business rule, which is why it does not come from
    /// IBusinessRulesProvider: those values are frozen onto bookings and must never mean anything else.
    /// </param>
    public static AttentionQueueDto Build(
        IReadOnlyCollection<LiveDispute> liveDisputes,
        IReadOnlyDictionary<Id, string> disputeSubtitles,
        IReadOnlyCollection<PendingDealerApplication> pendingApplications,
        decimal slaWarningThreshold,
        int slaHours,
        DateTimeOffset now,
        MoneyAttention? money = null)
    {
        ArgumentNullException.ThrowIfNull(liveDisputes);
        ArgumentNullException.ThrowIfNull(disputeSubtitles);
        ArgumentNullException.ThrowIfNull(pendingApplications);

        var items = new List<AttentionItemDto>();

        foreach (var dispute in liveDisputes)
        {
            var isOverdue = now >= dispute.SlaDeadline;
            items.Add(new AttentionItemDto(
                Id: $"dispute:{dispute.TicketId.Value}",
                Kind: isOverdue ? Kinds.DisputeOverdue : Kinds.DisputeOpen,
                Severity: Severity(isOverdue, dispute.OpenedAt, dispute.SlaDeadline, slaWarningThreshold, now),
                Count: 1,
                SubjectIds: [dispute.TicketId.Value],
                Subtitle: disputeSubtitles.GetValueOrDefault(dispute.TicketId),
                Description: dispute.Reason,
                SlaStartedAt: dispute.OpenedAt,
                SlaDeadlineAt: dispute.SlaDeadline,
                IsOverdue: isOverdue));
        }

        // The design shows pending applications as ONE row ("3 dealer applications are approaching the
        // 48-hour SLA"), not one row each, and only once they are actually at risk. An application
        // submitted an hour ago is not work an admin is behind on.
        var atRisk = pendingApplications
            .Where(application => IsAtRisk(application.SubmittedAt, application.ReviewDueAt, slaWarningThreshold, now))
            .OrderBy(application => application.ReviewDueAt)
            .ToList();

        if (atRisk.Count > 0)
        {
            var earliest = atRisk[0];
            var anyOverdue = atRisk.Any(application => now >= application.ReviewDueAt);
            items.Add(new AttentionItemDto(
                Id: "dealer-applications",
                Kind: Kinds.DealerApplicationsAtRisk,
                Severity: anyOverdue ? Severities.Overdue : Severities.Warning,
                Count: atRisk.Count,
                SubjectIds: [.. atRisk.Select(application => application.DealerId.Value)],
                // Names rather than ids: this line is read, not clicked through.
                Subtitle: string.Join(", ", atRisk.Take(3).Select(application => application.BusinessName)) +
                          (atRisk.Count > 3 ? $" and {(atRisk.Count - 3).ToString(CultureInfo.InvariantCulture)} more" : string.Empty),
                Description: null,
                // The row is judged by the application closest to breaching, so the meter and the
                // countdown describe the most urgent one rather than an average of the group.
                SlaStartedAt: earliest.SubmittedAt,
                SlaDeadlineAt: earliest.ReviewDueAt,
                IsOverdue: anyOverdue));
        }

        AddMoney(items, money ?? MoneyAttention.None);

        // Overdue work first; then whatever runs out of time soonest, because each of those is a promise
        // the platform made and can still keep; then money a human has to look at, which has no clock —
        // a refused refund is sent again by the payment sweep on its own, so it loses nothing by waiting
        // behind a live deadline; then money that is only being watched until the rules give it an exit.
        // Inside a band with no clock, whatever has waited longest. An admin reads top-down.
        var ordered = items
            .OrderBy(Band)
            .ThenBy(item => item.SlaDeadlineAt)
            .ThenBy(item => item.SlaStartedAt)
            .ToList();

        return new AttentionQueueDto(
            // The instant this panel speaks for. Each panel is its own request now, so each carries
            // its own freshness rather than borrowing one snapshot's.
            GeneratedAt: now,
            SlaHours: slaHours,
            OpenCount: ordered.Count,
            OverdueCount: ordered.Count(item => item.IsOverdue),
            Items: ordered);
    }

    /// <summary>
    /// The Payments context's rows (payments Phase 4b). None has a deadline: nobody has frozen one for
    /// money owed back, and inventing one would be a business number nobody decided. Grouped where there
    /// is a list to open (the refunds queue, the payments list), one per booking where each is its own
    /// decision. A subtitle carries booking references only — the console words the count.
    /// </summary>
    private static void AddMoney(List<AttentionItemDto> items, MoneyAttention money)
    {
        if (money.FailedRefunds.Count > 0)
        {
            items.Add(new AttentionItemDto(
                Id: "refunds-failed",
                Kind: Kinds.RefundFailed,
                // Money still owed that the provider refused: a human has to look, so never merely Info.
                Severity: Severities.Warning,
                Count: money.FailedRefunds.Count,
                SubjectIds: [.. money.FailedRefunds.Select(refund => refund.RefundId)],
                Subtitle: References(money.FailedRefunds.Select(refund => refund.BookingReference)),
                Description: null,
                SlaStartedAt: money.FailedRefunds.Min(refund => refund.RequestedAt),
                SlaDeadlineAt: null,
                IsOverdue: false));
        }

        if (money.OwedOrphans.Count > 0)
        {
            items.Add(new AttentionItemDto(
                Id: "orphaned-captures",
                Kind: Kinds.OrphanedCaptureOwed,
                Severity: Severities.Info,
                Count: money.OwedOrphans.Count,
                SubjectIds: [.. money.OwedOrphans.Select(orphan => orphan.PaymentId)],
                Subtitle: References(money.OwedOrphans.Select(orphan => orphan.BookingReference)),
                Description: null,
                SlaStartedAt: money.OwedOrphans.Min(orphan => orphan.OrphanedAt),
                SlaDeadlineAt: null,
                IsOverdue: false));
        }

        foreach (var held in money.HeldDeposits)
        {
            items.Add(new AttentionItemDto(
                Id: $"deposit:{held.BookingId}",
                Kind: Kinds.DepositAwaitingDecision,
                // Nothing an administrator can do until payments Phase 8: the row makes the money visible.
                Severity: Severities.Info,
                Count: 1,
                SubjectIds: [held.BookingId],
                Subtitle: held.Subtitle,
                Description: null,
                SlaStartedAt: held.HeldSince,
                SlaDeadlineAt: null,
                IsOverdue: false));
        }
    }

    /// <summary>Up to three booking references, the ones a human reads first; the console states the count.</summary>
    private static string? References(IEnumerable<string?> references)
    {
        var shown = references.OfType<string>().Distinct(StringComparer.Ordinal).Take(3).ToList();
        return shown.Count == 0 ? null : string.Join(" · ", shown);
    }

    private static int Band(AttentionItemDto item) =>
        item.IsOverdue ? 0
        : item.SlaDeadlineAt is not null ? 1
        : item.Severity == Severities.Warning ? 2
        : 3;

    private static string Severity(
        bool isOverdue,
        DateTimeOffset startedAt,
        DateTimeOffset deadlineAt,
        decimal threshold,
        DateTimeOffset now)
    {
        if (isOverdue)
            return Severities.Overdue;

        return IsAtRisk(startedAt, deadlineAt, threshold, now) ? Severities.Warning : Severities.Info;
    }

    private static bool IsAtRisk(
        DateTimeOffset startedAt,
        DateTimeOffset deadlineAt,
        decimal threshold,
        DateTimeOffset now)
    {
        if (now >= deadlineAt)
            return true;

        var window = deadlineAt - startedAt;
        // A zero or inverted window would mean the deadline was already reached, which the check above
        // has ruled out; guard anyway rather than dividing by zero on bad data.
        if (window <= TimeSpan.Zero)
            return true;

        var elapsed = now - startedAt;
        if (elapsed <= TimeSpan.Zero)
            return false;

        return (decimal)(elapsed.TotalSeconds / window.TotalSeconds) >= threshold;
    }
}

/// <summary>A deposit awaiting a decision, with its booking's label.</summary>
public sealed record HeldDepositRow(Guid BookingId, string? Subtitle, DateTimeOffset HeldSince);

/// <summary>What the Payments context owes a human, for the work queue (payments Phase 4b).</summary>
public sealed record MoneyAttention(
    IReadOnlyCollection<FailedRefundItem> FailedRefunds,
    IReadOnlyCollection<OwedOrphanItem> OwedOrphans,
    IReadOnlyCollection<HeldDepositRow> HeldDeposits)
{
    /// <summary>Nothing owed to anybody's attention.</summary>
    public static readonly MoneyAttention None = new([], [], []);
}
