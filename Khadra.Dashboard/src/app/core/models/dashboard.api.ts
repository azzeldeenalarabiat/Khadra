/**
 * The wire shapes of the admin dashboard endpoints — one per panel.
 *
 * A faithful mirror of the DTOs and nothing more: no colours, no icons, no routes and no
 * pre-formatted strings, because the API deliberately does not send any. Turning these facts into
 * something the design can render is the presenter's job, not this file's.
 *
 * These used to be one `AdminDashboard` snapshot. It was split because the navigation rail reads two
 * of these numbers on every admin screen and was paying for the work queue, the trend and the audit
 * feed to get them — and because, being one root resource fetched once, those numbers were frozen at
 * the first paint of the session.
 *
 * Every response carries `generatedAt`. With one snapshot there was one "now"; with several, each
 * panel has its own, and a panel that is minutes stale can say so.
 */

/** Every panel states the instant it speaks for. */
interface PanelResponse {
  readonly generatedAt: string;
}

export interface DealerCounts extends PanelResponse {
  readonly total: number;
  readonly trading: number;
  readonly pendingReview: number;
  readonly clarificationNeeded: number;
  readonly rejected: number;
  readonly suspended: number;
}

export interface BookingCounts extends PanelResponse {
  readonly total: number;
  /** Bookings taken on the platform's local reporting day, not a UTC one. */
  readonly today: number;
  readonly active: number;
  readonly pendingApproval: number;
}

export interface CustomerCounts extends PanelResponse {
  readonly total: number;
  readonly verified: number;
  readonly pendingVerification: number;
  readonly suspended: number;
}

export interface DisputeCounts extends PanelResponse {
  readonly open: number;
  readonly underReview: number;
  readonly overdue: number;
  readonly resolvedRecently: number;
  /** The window `resolvedRecently` was measured over. It travels with the figure, never assumed. */
  readonly resolvedWindowDays: number;
}

/**
 * What is sitting with the platform: the rail badges it with, but it is not named for the rail.
 * Fetched on every navigation and after every decision, which is what keeps a badge true rather than
 * merely live-looking.
 */
export interface AdminWorkload extends PanelResponse {
  /** PendingReview only. An application sent back for clarification is the dealer's move, not ours. */
  readonly dealerApplicationsAwaitingReview: number;
  readonly liveDisputes: number;
}

export interface DailyCount {
  readonly date: string;
  readonly count: number;
}

export interface BookingTrend extends PanelResponse {
  readonly from: string;
  readonly to: string;
  readonly points: readonly DailyCount[];
  /** Null when the preceding window was empty: "+100%" against zero is a made-up number. */
  readonly changePercent: number | null;
}

export interface AttentionItem {
  readonly id: string;
  /** A client that meets an unfamiliar kind must still render the row. */
  readonly kind: string;
  readonly severity: 'Overdue' | 'Warning' | 'Info' | string;
  readonly count: number;
  readonly subjectIds: readonly string[];
  readonly subtitle: string | null;
  readonly description: string | null;
  readonly slaStartedAt: string;
  readonly slaDeadlineAt: string;
  readonly isOverdue: boolean;
}

export interface AttentionQueue extends PanelResponse {
  /** The SLA in force when this was generated. Each row is judged against its OWN frozen deadline. */
  readonly slaHours: number;
  readonly openCount: number;
  readonly overdueCount: number;
  readonly items: readonly AttentionItem[];
}

export interface ActivityEntry {
  readonly id: string;
  readonly occurredAt: string;
  readonly actorName: string;
  readonly action: string;
  readonly entityType: string;
  readonly subjectLabel: string;
}

export interface ActivityFeed extends PanelResponse {
  readonly entries: readonly ActivityEntry[];
}
