import { AttentionItem, AttentionQueue } from '../models/dashboard.api';
import { DealerDashboard } from '../models/dealer-console.api';
import { NotificationItem } from '../models/notifications.api';
import { Tone } from '../models/console.models';
import { IconName } from '../../shared/icon/icon-paths';
import { relativeTime } from '../i18n/relative-time';
import { Translate, toQueueItems } from './dashboard.presenter';

/**
 * The sentence for one of the reader's notifications, composed here rather than stored.
 *
 * Every kind in the server's vocabulary has a case; an unknown one falls back to something true rather than to an
 * empty line, because a new kind should degrade, not disappear. Named PARAMETERS rather than a template literal:
 * Arabic does not put the actor and the object where English puts them, so a sentence is one message with two holes
 * in it, not three pieces concatenated. And what the reader did themselves has sentences of its own (pre-launch item
 * 218): Arabic does not drop «أنت» into the actor's place — «قبل أنت KH-…» — it says «أنت من قبِل KH-…».
 */
export function notificationSentence(item: NotificationItem, t: Translate): string {
  const mine = item.isMine;
  const who = mine ? t('notifications.you') : item.actorName;
  const what = item.subjectReference ?? t('notifications.aBooking');
  const parts = { who, what };

  switch (item.kind) {
    // The one kind raised from outside the dealership. Its row carries no actor on purpose -- a customer's name is
    // never copied into this table -- so it does not use `who`.
    case 'BookingRequested':
      return t('notifications.customerRequested', { what });
    case 'BookingApproved':
      return mine ? t('notifications.approvedByYou', { what }) : t('notifications.approved', parts);
    case 'BookingRejected':
      return mine ? t('notifications.rejectedByYou', { what }) : t('notifications.rejected', parts);
    case 'BookingPickedUp':
      return mine ? t('notifications.recordedPickupByYou', { what }) : t('notifications.recordedPickup', parts);
    case 'BookingReturned':
      return mine ? t('notifications.recordedReturnByYou', { what }) : t('notifications.recordedReturn', parts);
    case 'BookingConfirmed':
      return t('notifications.customerPaid', { what });
    case 'DealerApproved':
      return t('notifications.dealerApproved');
    case 'DealerRejected':
      return t('notifications.dealerRejected');
    case 'DealerClarificationRequested':
      return t('notifications.dealerClarification');
    case 'DealerSuspended':
      return t('notifications.dealerSuspended');
    case 'DealerReactivated':
      return t('notifications.dealerReactivated');
    case 'StaffReactivated':
      return mine ? t('notifications.staffReactivatedByYou') : t('notifications.staffReactivated', { who });
    // Raised for the employee whose access changed, by somebody else: never the reader's own act.
    case 'ReportAccessGranted':
      return t('notifications.reportAccessGranted', { who });
    case 'ReportAccessRevoked':
      return t('notifications.reportAccessRevoked', { who });
    default:
      return mine ? t('notifications.updatedByYou', { what }) : t('notifications.updated', parts);
  }
}

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
 * Whether a row asks something of the reader. One with no clock and no warning is WATCHED money — a
 * capture being refunded — which the dashboard lists and the bell does not: the badge counts the bell's
 * rows, and a count nobody can clear would teach an administrator to ignore the bell, refused refunds
 * included. The offices' money the payables ledger holds back is a Warning: a person has to look.
 */
const asksForAction = (item: AttentionItem): boolean =>
  item.slaDeadlineAt !== null || item.severity !== 'Info';

/**
 * What is waiting on an administrator.
 *
 * The attention queue, through the same presenter the dashboard's "Requires attention" panel uses,
 * so the bell and the panel below it cannot word the same item differently or count it differently.
 * The bell leaves out only what nobody has to act on (`asksForAction`).
 */
export function toAdminNotifications(
  queue: AttentionQueue | null,
  now: number,
  t: Translate,
): readonly NotificationRow[] {
  if (!queue) return [];
  return toQueueItems({ ...queue, items: queue.items.filter(asksForAction) }, now, t).map((item) => ({
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
