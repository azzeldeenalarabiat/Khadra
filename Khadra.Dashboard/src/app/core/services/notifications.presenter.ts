import { AttentionQueue } from '../models/dashboard.api';
import { DealerDashboard } from '../models/dealer-console.api';
import { Tone } from '../models/console.models';
import { IconName } from '../../shared/icon/icon-paths';
import { relativeTime } from '../i18n/relative-time';
import { Translate, toQueueItems } from './dashboard.presenter';

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
  localeTag: string,
): readonly NotificationRow[] {
  if (!dashboard) return [];
  const rows: NotificationRow[] = [];

  if (dashboard.bookings.overdueReturns > 0) {
    const n = dashboard.bookings.overdueReturns;
    rows.push({
      id: 'overdue-returns',
      // A PLURAL message, not an English `n === 1` ternary: Arabic has six forms, and choosing
      // between two of them in TypeScript picks the wrong one for every count from two upwards.
      title: t('notifications.carsOverdue', { count: n }),
      detail: t('notifications.pastTheEndOf'),
      when: t('notifications.overdue'),
      tone: 'bad',
      icon: 'warning-circle',
      route: '/dealer/bookings',
    });
  }

  if (dashboard.bookings.requested > 0) {
    const n = dashboard.bookings.requested;
    rows.push({
      id: 'pending-requests',
      title: t('notifications.requestsWaiting', { count: n }),
      // The oldest is the one closest to expiring, so it is the fact worth carrying.
      detail: dashboard.bookings.oldestRequestedAt
        ? t('notifications.oldestAndExpiry', {
            when: relativeTime(dashboard.bookings.oldestRequestedAt, now, localeTag),
          })
        : t('notifications.aRequestExpiresWhen'),
      when: t('notifications.toAnswer'),
      tone: 'warn',
      icon: 'calendar-check',
      route: '/dealer/bookings',
    });
  }

  // The vehicle and the customer can both be gone by the time a handover is due. The server says so
  // with a null rather than an English phrase, and the console words it.
  const vehicle = (label: string | null): string =>
    label ?? t('dealerBookings.vehicleNoLongerListed');
  const customer = (name: string | null): string => name ?? t('common.customerAccountClosed');

  for (const pickup of dashboard.upcomingPickups) {
    rows.push({
      id: `pickup:${pickup.bookingId}`,
      title: t('notifications.pickupRow', { vehicle: vehicle(pickup.vehicleLabel) }),
      detail: `${customer(pickup.customerName)} · ${pickup.reference}`,
      // Forwards as well as backwards: an upcoming pickup reads "in 3 hr", not "Just now".
      when: relativeTime(pickup.when, now, localeTag),
      tone: pickup.isOverdue ? 'bad' : 'accent',
      icon: pickup.pickupMethod === 'Delivery' ? 'moped' : 'map-pin',
      route: `/dealer/bookings/${pickup.bookingId}`,
    });
  }

  for (const back of dashboard.upcomingReturns) {
    rows.push({
      id: `return:${back.bookingId}`,
      title: t('notifications.returnRow', { vehicle: vehicle(back.vehicleLabel) }),
      detail: `${customer(back.customerName)} · ${back.reference}`,
      when: relativeTime(back.when, now, localeTag),
      tone: back.isOverdue ? 'bad' : 'ok',
      icon: 'arrow-u-down-left',
      route: `/dealer/bookings/${back.bookingId}`,
    });
  }

  return rows;
}
