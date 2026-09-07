import { AttentionQueue } from '../models/dashboard.api';
import { DealerDashboard } from '../models/dealer-console.api';
import { Tone } from '../models/console.models';
import { IconName } from '../../shared/icon/icon-paths';
import { Translate, relativeTime, toQueueItems } from './dashboard.presenter';

/** One line in the notifications panel. Every field is derived from a record the server sent. */
export interface NotificationRow {
  readonly id: string;
  readonly title: string;
  readonly detail: string;
  readonly when: string;
  readonly tone: Tone;
  readonly icon: IconName;
  readonly route: string;
}

/**
 * What is waiting on an administrator.
 *
 * The attention queue, through the same presenter the dashboard's "Requires attention" panel uses,
 * so the bell and the panel below it cannot word the same item differently or count it differently.
 */
export function toAdminNotifications(
  queue: AttentionQueue | null,
  now: number,
  t: Translate,
): readonly NotificationRow[] {
  if (!queue) return [];
  return toQueueItems(queue, now, t).map((item) => ({
    id: item.id,
    title: item.title,
    // The entity is the more useful of the two when both are present: it names the record.
    detail: item.entity || item.description,
    when: item.sla,
    tone: item.tone,
    icon: item.tone === 'bad' ? 'warning-circle' : 'clock',
    route: item.route,
  }));
}

/**
 * What is waiting on a dealership.
 *
 * Built from the dashboard payload the console already holds, so the bell costs no extra request.
 * Ordered by urgency rather than by time — what is already late first, because that is the order the
 * reader has to work in. A count of zero produces no row at all: "0 requests waiting" is not news.
 */
export function toDealerNotifications(
  dashboard: DealerDashboard | null,
  now: number,
  t: Translate,
): readonly NotificationRow[] {
  if (!dashboard) return [];
  const rows: NotificationRow[] = [];

  if (dashboard.bookings.overdueReturns > 0) {
    const n = dashboard.bookings.overdueReturns;
    rows.push({
      id: 'overdue-returns',
      title: `${n} ${n === 1 ? 'car is' : 'cars are'} overdue back`,
      detail: 'Past the end of the rental period and not yet returned.',
      when: 'Overdue',
      tone: 'bad',
      icon: 'warning-circle',
      route: '/dealer/bookings',
    });
  }

  if (dashboard.bookings.requested > 0) {
    const n = dashboard.bookings.requested;
    rows.push({
      id: 'pending-requests',
      title: `${n} booking ${n === 1 ? 'request is' : 'requests are'} waiting`,
      // The oldest is the one closest to expiring, so it is the fact worth carrying.
      detail: dashboard.bookings.oldestRequestedAt
        ? `Oldest ${relativeTime(dashboard.bookings.oldestRequestedAt, now, t)}. A request expires when its rental date arrives unanswered.`
        : 'A request expires when its rental date arrives unanswered.',
      when: 'To answer',
      tone: 'warn',
      icon: 'calendar-check',
      route: '/dealer/bookings',
    });
  }

  for (const pickup of dashboard.upcomingPickups) {
    rows.push({
      id: `pickup:${pickup.bookingId}`,
      title: `Pickup — ${pickup.vehicleLabel}`,
      detail: `${pickup.customerName} · ${pickup.reference}`,
      when: relativeTime(pickup.when, now, t),
      tone: pickup.isOverdue ? 'bad' : 'accent',
      icon: pickup.pickupMethod === 'Delivery' ? 'moped' : 'map-pin',
      route: `/dealer/bookings/${pickup.bookingId}`,
    });
  }

  for (const back of dashboard.upcomingReturns) {
    rows.push({
      id: `return:${back.bookingId}`,
      title: `Return — ${back.vehicleLabel}`,
      detail: `${back.customerName} · ${back.reference}`,
      when: relativeTime(back.when, now, t),
      tone: back.isOverdue ? 'bad' : 'ok',
      icon: 'arrow-u-down-left',
      route: `/dealer/bookings/${back.bookingId}`,
    });
  }

  return rows;
}
