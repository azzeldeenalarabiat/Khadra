/**
 * The wire shape of `GET /api/v1/admin/dashboard`.
 *
 * A faithful mirror of AdminDashboardDto and nothing more: no colours, no icons, no routes and no
 * pre-formatted strings, because the API deliberately does not send any. Turning these facts into
 * something the design can render is the presenter's job, not this file's.
 *
 * `finance` and `moneyInMotion` are null until the Payments context exists. Null means "this
 * deployment cannot answer that", which is not the same as zero and must not render as zero.
 */

export interface DealerCounts {
  readonly total: number;
  readonly trading: number;
  readonly pendingReview: number;
  readonly clarificationNeeded: number;
  readonly rejected: number;
  readonly suspended: number;
}

export interface BookingCounts {
  readonly total: number;
  readonly today: number;
  readonly active: number;
  readonly pendingApproval: number;
}

export interface CustomerCounts {
  readonly total: number;
  readonly verified: number;
  readonly pendingVerification: number;
  readonly suspended: number;
}

export interface DisputeCounts {
  readonly open: number;
  readonly underReview: number;
  readonly overdue: number;
  readonly resolvedRecently: number;
  readonly resolvedWindowDays: number;
}

export interface Money {
  readonly amount: number;
  readonly currency: string;
}

export interface FinanceSummary {
  readonly grossBookingValue: Money;
  readonly commission: Money;
  readonly dealerPayouts: Money;
  readonly refunds: Money;
  readonly periodFrom: string;
  readonly periodTo: string;
}

export interface MoneyInMotion {
  readonly gross: Money;
  readonly commission: Money;
  readonly dealerPayouts: Money;
}

export interface DailyCount {
  readonly date: string;
  readonly count: number;
}

export interface BookingTrend {
  readonly from: string;
  readonly to: string;
  readonly points: readonly DailyCount[];
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

export interface AttentionQueue {
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

export interface AdminDashboard {
  readonly generatedAt: string;
  readonly adminSlaHours: number;
  readonly dealers: DealerCounts;
  readonly bookings: BookingCounts;
  readonly customers: CustomerCounts;
  readonly disputes: DisputeCounts;
  readonly finance: FinanceSummary | null;
  readonly moneyInMotion: MoneyInMotion | null;
  readonly bookingTrend: BookingTrend;
  readonly attentionQueue: AttentionQueue;
  readonly recentActivity: readonly ActivityEntry[];
}
