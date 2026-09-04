import { IconName } from '../../shared/icon/icon-paths';
import { Tone } from '../models/console.models';
import {
  ActivityEntry,
  AttentionItem,
  AttentionQueue,
  BookingCounts,
  BookingTrend,
  CustomerCounts,
  DealerCounts,
  DisputeCounts,
} from '../models/dashboard.api';
import { KpiCard, QueueItem } from '../data/dashboard.data';

/**
 * Turns the API responses into what the design renders.
 *
 * Everything the server refuses to decide is decided here: which colour a severity is, which icon an
 * action gets, which route a queue row opens, how a deadline reads as "13h over", and how tall a bar
 * is. Keeping it in pure functions means the countdowns can be recomputed on a timer without
 * refetching, and that all of it is testable without a component.
 *
 * Each panel arrives on its own now, so every function here takes the one response it needs and a
 * card is built the moment its own answer lands.
 */

const UNAVAILABLE = '—';

const formatCount = (value: number): string => value.toLocaleString('en-US');

/** The four count responses, each present only once its own request has answered. */
export interface KpiSources {
  readonly dealers: DealerCounts | undefined;
  readonly bookings: BookingCounts | undefined;
  readonly customers: CustomerCounts | undefined;
  readonly disputes: DisputeCounts | undefined;
}

/**
 * The KPI row, built from whichever answers have arrived.
 *
 * A card is omitted until its own response lands rather than the row waiting on the slowest, and no
 * card is ever rendered from a partial or assumed figure.
 *
 * There is no Revenue card. It used to be here showing dashes, fed by a permanently-null slice of the
 * composite — a request made on every load to be told the Payments context does not exist. The money
 * panel below states that once, in words; a KPI card of dashes said it four times and looked like a
 * figure that had failed to load.
 */
export function toKpiCards(sources: KpiSources): readonly KpiCard[] {
  const { dealers, bookings, customers, disputes } = sources;
  const cards: KpiCard[] = [];

  if (dealers) {
    cards.push({
      label: 'Total dealers',
      icon: 'storefront',
      main: formatCount(dealers.total),
      route: '/dealers',
      subs: [
        { k: 'Trading', v: formatCount(dealers.trading) },
        { k: 'Pending review', v: formatCount(dealers.pendingReview), tone: 'warn' as Tone },
        { k: 'Suspended', v: formatCount(dealers.suspended), tone: 'bad' as Tone },
      ],
    });
  }

  if (bookings) {
    cards.push({
      label: 'Bookings',
      icon: 'calendar-check',
      main: formatCount(bookings.total),
      route: '/bookings',
      subs: [
        { k: 'Today', v: formatCount(bookings.today) },
        { k: 'Active', v: formatCount(bookings.active) },
        { k: 'Pending', v: formatCount(bookings.pendingApproval), tone: 'warn' as Tone },
      ],
    });
  }

  if (customers) {
    cards.push({
      label: 'Customers',
      icon: 'users-three',
      main: formatCount(customers.total),
      route: '/customers',
      subs: [
        { k: 'Verified', v: formatCount(customers.verified) },
        {
          k: 'Pending verification',
          v: formatCount(customers.pendingVerification),
          tone: 'warn' as Tone,
        },
        { k: 'Suspended', v: formatCount(customers.suspended) },
      ],
    });
  }

  if (disputes) {
    cards.push({
      label: 'Disputes',
      icon: 'scales',
      main: formatCount(disputes.open + disputes.underReview),
      route: '/disputes',
      subs: [
        { k: 'Pending admin', v: formatCount(disputes.open), tone: 'warn' as Tone },
        { k: 'Overdue', v: formatCount(disputes.overdue), tone: 'bad' as Tone },
        {
          // The window travels with the figure, from the server. "4 resolved" means nothing without
          // "in 30 days", and the console must not be the thing that remembers which 30.
          k: `Resolved ${disputes.resolvedWindowDays}d`,
          v: formatCount(disputes.resolvedRecently),
        },
      ],
    });
  }

  return cards;
}

/** Severity is the server's word; the colour is ours. */
const severityTone = (severity: string): Tone => {
  if (severity === 'Overdue') return 'bad';
  if (severity === 'Warning') return 'warn';
  return 'dim';
};

/**
 * Where a row leads and what its button says. An unknown kind still renders — it just opens the
 * dashboard's own queue instead of a screen we have not been told about — which is what lets the
 * Payments kinds arrive later without a frontend release.
 */
const kindTarget = (kind: string): { route: string; action: string } => {
  switch (kind) {
    case 'DisputeOverdue':
    case 'DisputeOpen':
      return { route: '/disputes', action: 'Resolve' };
    case 'DealerApplicationsAtRisk':
      return { route: '/dealers', action: 'Review' };
    default:
      return { route: '/dashboard', action: 'Open' };
  }
};

/** "Overdue" / "SLA 41h" — the short badge the design puts above each row. */
const severityLabel = (item: AttentionItem, now: number): string => {
  if (item.isOverdue) return 'Overdue';
  const hoursLeft = Math.max(0, Math.round((Date.parse(item.slaDeadlineAt) - now) / 3_600_000));
  return `SLA ${hoursLeft}h`;
};

/** "13h over", "7h left", "2d left". */
export function slaLabel(item: AttentionItem, now: number): string {
  const deadline = Date.parse(item.slaDeadlineAt);
  const deltaMinutes = Math.round(Math.abs(deadline - now) / 60_000);
  const suffix = deadline <= now ? 'over' : 'left';

  if (deltaMinutes < 60) return `${deltaMinutes}m ${suffix}`;
  const hours = Math.round(deltaMinutes / 60);
  if (hours < 48) return `${hours}h ${suffix}`;
  return `${Math.round(hours / 24)}d ${suffix}`;
}

/** How full the meter is. Overdue pins at 100 rather than running off the end. */
export function slaPercent(item: AttentionItem, now: number): number {
  const start = Date.parse(item.slaStartedAt);
  const deadline = Date.parse(item.slaDeadlineAt);
  if (!(deadline > start)) return 100;
  const elapsed = ((now - start) / (deadline - start)) * 100;
  return Math.max(0, Math.min(100, Math.round(elapsed)));
}

/** The one-line summary the design shows in bold on each row. */
const queueTitle = (item: AttentionItem, now: number): string => {
  if (item.kind === 'DealerApplicationsAtRisk') {
    const noun = item.count === 1 ? 'dealer application is' : 'dealer applications are';
    return item.isOverdue
      ? `${item.count} ${noun} past the review SLA`
      : `${item.count} ${noun} approaching the review SLA`;
  }

  const ageHours = Math.max(0, Math.round((now - Date.parse(item.slaStartedAt)) / 3_600_000));
  if (item.kind === 'DisputeOverdue' || item.kind === 'DisputeOpen') {
    return ageHours < 1 ? 'New dispute opened' : `Dispute open for ${ageHours} hours`;
  }
  return item.subtitle ?? 'Needs attention';
};

export function toQueueItems(queue: AttentionQueue, now: number): readonly QueueItem[] {
  return queue.items.map((item) => {
    const target = kindTarget(item.kind);
    return {
      severity: severityLabel(item, now),
      tone: severityTone(item.severity),
      title: queueTitle(item, now),
      description: item.description ?? '',
      entity: item.subtitle ?? '',
      sla: slaLabel(item, now),
      percent: slaPercent(item, now),
      action: target.action,
      route: target.route,
    };
  });
}

/**
 * Bar heights as a percentage of the busiest day. The design's tallest bar sits at 88%, so the scale
 * stops there rather than at 100 and the chart keeps the headroom it was drawn with. A day with no
 * bookings gets no bar, which is the honest reading; a day with one gets a visible stub.
 */
export function toTrendHeights(trend: BookingTrend): readonly number[] {
  const counts = trend.points.map((point) => point.count);
  const busiest = Math.max(0, ...counts);
  if (busiest === 0) return counts.map(() => 0);
  return counts.map((count) => (count === 0 ? 0 : Math.max(6, Math.round((count / busiest) * 88))));
}

// toMoneyRows is gone with the composite's null finance slice. It mapped three permanently-null
// figures onto three dashed rows, which is a lot of machinery to say "the Payments context is not
// built" — the panel now says exactly that, once, and calls nothing to find it out. When Payments
// ships it arrives as its own endpoint and this comes back as a real mapping.

const ACTIVITY_ICONS: Readonly<Record<string, IconName>> = {
  DealerApproved: 'check-circle',
  DealerRejected: 'x-circle',
  DealerClarificationRequested: 'warning',
  DealerSuspended: 'prohibit',
  DealerReactivated: 'check-circle',
  CustomerSuspended: 'user-gear',
  CustomerReactivated: 'user-gear',
  DisputeOpened: 'scales',
  DisputeAssigned: 'scales',
  DisputeResolved: 'gavel',
  BusinessRuleChanged: 'sliders-horizontal',
  ReviewHidden: 'eye-slash',
  ReviewRestored: 'star',
  AdminInvited: 'shield-check',
  AdminDeactivated: 'user-minus',
  BookingCancelledByAdmin: 'calendar-blank',
  BookingExpired: 'calendar-blank',
  BookingMarkedNoShow: 'warning-circle',
};

// The sentence is composed here rather than on the server, so the wording (and one day the language)
// stays with the interface that shows it.
const ACTIVITY_VERBS: Readonly<Record<string, string>> = {
  DealerApproved: 'approved dealer',
  DealerRejected: 'rejected dealer',
  DealerClarificationRequested: 'asked for clarification from',
  DealerSuspended: 'suspended dealer',
  DealerReactivated: 'reactivated dealer',
  CustomerSuspended: 'suspended customer',
  CustomerReactivated: 'reactivated customer',
  DisputeOpened: 'opened dispute',
  DisputeAssigned: 'took the dispute',
  DisputeResolved: 'resolved dispute',
  BusinessRuleChanged: 'changed setting',
  ReviewHidden: 'hid review',
  ReviewRestored: 'restored review',
  AdminInvited: 'invited admin',
  AdminDeactivated: 'deactivated admin',
  BookingCancelledByAdmin: 'cancelled booking',
  BookingExpired: 'expired booking',
  BookingMarkedNoShow: 'recorded a no-show on',
};

export function relativeTime(iso: string, now: number): string {
  const minutes = Math.round((now - Date.parse(iso)) / 60_000);
  if (minutes < 1) return 'Just now';
  if (minutes < 60) return `${minutes} min ago`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.round(hours / 24);
  if (days === 1) return 'Yesterday';
  return `${days}d ago`;
}

export interface ActivityRow {
  readonly icon: IconName;
  readonly text: string;
  readonly ts: string;
}

export function toActivityRows(
  entries: readonly ActivityEntry[],
  now: number,
): readonly ActivityRow[] {
  return entries.map((entry) => ({
    icon: ACTIVITY_ICONS[entry.action] ?? 'info',
    // An unmapped action still reads sensibly: the raw name is better than an empty line.
    text: `${entry.actorName} ${ACTIVITY_VERBS[entry.action] ?? entry.action} ${entry.subjectLabel}`,
    ts: relativeTime(entry.occurredAt, now),
  }));
}

export const formatChangePercent = (change: number | null): string =>
  change === null ? UNAVAILABLE : `${change > 0 ? '+' : ''}${change.toFixed(1)}%`;
