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
import { TranslationKey } from '../i18n/en';
import { MessageParams } from '../i18n/language';

/** Passed in rather than injected: these are pure functions, and their spec calls them directly. */
export type Translate = (key: TranslationKey, params?: MessageParams) => string;
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

/**
 * The four count responses, each present only once its own request has answered.
 *
 * Null as well as undefined: the screen reads each resource through `loaded()`, which answers null
 * both while a request is in flight and after one has failed. Either way there is no figure, and a
 * card without a figure is not drawn.
 */
export interface KpiSources {
  readonly dealers: DealerCounts | null | undefined;
  readonly bookings: BookingCounts | null | undefined;
  readonly customers: CustomerCounts | null | undefined;
  readonly disputes: DisputeCounts | null | undefined;
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
export function toKpiCards(sources: KpiSources, t: Translate): readonly KpiCard[] {
  const { dealers, bookings, customers, disputes } = sources;
  const cards: KpiCard[] = [];

  if (dealers) {
    cards.push({
      label: t('kpi.totalDealers'),
      icon: 'storefront',
      main: formatCount(dealers.total),
      route: '/dealers',
      subs: [
        { k: t('kpi.trading'), v: formatCount(dealers.trading) },
        { k: t('kpi.pendingReview'), v: formatCount(dealers.pendingReview), tone: 'warn' as Tone },
        { k: t('kpi.suspended'), v: formatCount(dealers.suspended), tone: 'bad' as Tone },
      ],
    });
  }

  if (bookings) {
    cards.push({
      label: t('kpi.bookings'),
      icon: 'calendar-check',
      main: formatCount(bookings.total),
      route: '/bookings',
      subs: [
        { k: t('kpi.today'), v: formatCount(bookings.today) },
        { k: t('kpi.active'), v: formatCount(bookings.active) },
        { k: t('kpi.pending'), v: formatCount(bookings.pendingApproval), tone: 'warn' as Tone },
      ],
    });
  }

  if (customers) {
    cards.push({
      label: t('kpi.customers'),
      icon: 'users-three',
      main: formatCount(customers.total),
      route: '/customers',
      subs: [
        { k: t('kpi.verified'), v: formatCount(customers.verified) },
        {
          k: t('kpi.pendingVerification'),
          v: formatCount(customers.pendingVerification),
          tone: 'warn' as Tone,
        },
        { k: t('kpi.suspended'), v: formatCount(customers.suspended) },
      ],
    });
  }

  if (disputes) {
    cards.push({
      label: t('kpi.disputes'),
      icon: 'scales',
      main: formatCount(disputes.open + disputes.underReview),
      route: '/disputes',
      subs: [
        { k: t('kpi.pendingAdmin'), v: formatCount(disputes.open), tone: 'warn' as Tone },
        { k: t('kpi.overdue'), v: formatCount(disputes.overdue), tone: 'bad' as Tone },
        {
          // The window travels with the figure, from the server. "4 resolved" means nothing without
          // "in 30 days", and the console must not be the thing that remembers which 30.
          k: t('kpi.resolvedInDays', { days: disputes.resolvedWindowDays }),
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
 * Where a row leads and what its button says.
 *
 * Straight to the RECORD, not to the list it lives in. The queue exists so an admin can act on the
 * thing in front of them, and every button used to land on `/disputes` — six rows each naming a
 * different ticket, all six leading to the same page, where the ticket had to be found again. The
 * API sends the ids in `subjectIds`; this uses them.
 *
 * A row standing for SEVERAL records (a dealer row can carry a count) still opens the list, because
 * there is no single record to open. An unknown kind still renders — it just opens the dashboard's
 * own queue instead of a screen we have not been told about — which is what lets the Payments kinds
 * arrive later without a frontend release.
 */
const kindTarget = (item: AttentionItem): { route: string; action: string } => {
  const only = item.subjectIds?.length === 1 ? item.subjectIds[0] : null;
  switch (item.kind) {
    case 'DisputeOverdue':
    case 'DisputeOpen':
      return { route: only ? `/disputes/${only}` : '/disputes', action: 'Resolve' };
    case 'DealerApplicationsAtRisk':
      return { route: only ? `/dealers/${only}` : '/dealers', action: 'Review' };
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
const queueTitle = (item: AttentionItem, now: number, t: Translate): string => {
  if (item.kind === 'DealerApplicationsAtRisk') {
    // One message per plural category, because Arabic needs six where English needs two.
    return t(
      item.isOverdue ? 'queue.applicationsOverdue' : 'queue.applicationsApproaching',
      { count: item.count },
    );
  }

  const ageHours = Math.max(0, Math.round((now - Date.parse(item.slaStartedAt)) / 3_600_000));
  if (item.kind === 'DisputeOverdue' || item.kind === 'DisputeOpen') {
    return ageHours < 1
      ? t('queue.newDispute')
      : t('queue.disputeOpenFor', { count: ageHours });
  }
  return item.subtitle ?? t('queue.needsAttention');
};

export function toQueueItems(
  queue: AttentionQueue,
  now: number,
  t: Translate,
): readonly QueueItem[] {
  return queue.items.map((item) => {
    const target = kindTarget(item);
    return {
      id: item.id,
      severity: severityLabel(item, now),
      tone: severityTone(item.severity),
      title: queueTitle(item, now, t),
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
 * One bar per day, carrying the figures it was drawn from.
 *
 * This used to return heights alone — fourteen bare percentages — and a reader had no way to tell
 * the chart apart from decoration: no dates, no counts, no scale, and the two quiet days rendered as
 * nothing at all, so a fortnight showed twelve bars. It was real data the whole time and looked
 * exactly like invented data, which is the thing this console is not allowed to do.
 *
 * Each bar now says which day it is and how many bookings that day had, so any figure on the chart
 * can be checked against the list behind it.
 */
export interface TrendBar {
  readonly date: string;
  readonly count: number;
  /** Percentage of the busiest day. The design's tallest bar sits at 88%, so the scale stops there. */
  readonly height: number;
  /** "Tue 2 Sept: 17 bookings" — what the bar is, in words, for a tooltip and a screen reader. */
  readonly label: string;
  /** The short axis tick under the bar. Only some bars get one; the rest would be a smear. */
  readonly tick: string;
}

export function toTrendBars(trend: BookingTrend): readonly TrendBar[] {
  const counts = trend.points.map((point) => point.count);
  const busiest = Math.max(0, ...counts);

  return trend.points.map((point, index) => {
    const day = new Date(point.date + 'T00:00:00');
    const height =
      busiest === 0 || point.count === 0
        ? 0
        : Math.max(6, Math.round((point.count / busiest) * 88));

    return {
      date: point.date,
      count: point.count,
      height,
      label: `${day.toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric', month: 'short' })}: ${point.count} ${point.count === 1 ? 'booking' : 'bookings'}`,
      // First, last, and roughly every fourth day between: enough to place a bar in the fortnight
      // without the labels running into each other.
      tick:
        index === 0 || index === trend.points.length - 1 || index % 4 === 0
          ? day.toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })
          : '',
    };
  });
}

/** The busiest day in the window, which is what every bar is drawn as a proportion of. */
export function busiestDay(trend: BookingTrend): number {
  return Math.max(0, ...trend.points.map((point) => point.count));
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
const ACTIVITY_VERBS: Readonly<Record<string, TranslationKey>> = {
  DealerApproved: 'activity.dealerApproved',
  DealerRejected: 'activity.dealerRejected',
  DealerClarificationRequested: 'activity.dealerClarification',
  DealerSuspended: 'activity.dealerSuspended',
  DealerReactivated: 'activity.dealerReactivated',
  CustomerSuspended: 'activity.customerSuspended',
  CustomerReactivated: 'activity.customerReactivated',
  DisputeOpened: 'activity.disputeOpened',
  DisputeAssigned: 'activity.disputeAssigned',
  DisputeResolved: 'activity.disputeResolved',
  BusinessRuleChanged: 'activity.settingChanged',
  ReviewHidden: 'activity.reviewHidden',
  ReviewRestored: 'activity.reviewRestored',
  AdminInvited: 'activity.adminInvited',
  AdminDeactivated: 'activity.adminDeactivated',
  BookingCancelledByAdmin: 'activity.bookingCancelled',
  BookingExpired: 'activity.bookingExpired',
  BookingMarkedNoShow: 'activity.bookingNoShow',
};

export function relativeTime(iso: string, now: number, t: Translate): string {
  const minutes = Math.round((now - Date.parse(iso)) / 60_000);
  if (minutes < 1) return t('time.justNow');
  if (minutes < 60) return t('time.minutesAgo', { count: minutes });
  const hours = Math.round(minutes / 60);
  if (hours < 24) return t('time.hoursAgo', { count: hours });
  const days = Math.round(hours / 24);
  if (days === 1) return t('time.yesterday');
  return t('time.daysAgo', { count: days });
}

export interface ActivityRow {
  readonly icon: IconName;
  readonly text: string;
  readonly ts: string;
}

export function toActivityRows(
  entries: readonly ActivityEntry[],
  now: number,
  t: Translate,
): readonly ActivityRow[] {
  return entries.map((entry) => ({
    icon: ACTIVITY_ICONS[entry.action] ?? 'info',
    // An unmapped action still reads sensibly: the raw name is better than an empty line.
    text: `${entry.actorName} ${verb(entry.action, t)} ${entry.subjectLabel}`,
    ts: relativeTime(entry.occurredAt, now, t),
  }));
}

/** An unmapped action still reads sensibly: the raw name beats an empty line. */
const verb = (action: string, t: Translate): string => {
  const key = ACTIVITY_VERBS[action];
  return key ? t(key) : action;
};

export const formatChangePercent = (change: number | null): string =>
  change === null ? UNAVAILABLE : `${change > 0 ? '+' : ''}${change.toFixed(1)}%`;
