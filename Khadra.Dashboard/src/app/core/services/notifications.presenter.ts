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
  // A customer is never named to an office: the row carries the server's English stand-in, which the console words in
  // the reader's own language (Wave 3, F55b), on the rows already stored as on new ones.
  // The stand-in `DealerTeamNotifier` stores for a customer, compared and never shown as it is.
  const byCustomer = item.actorName === 'A customer';
  const who = mine ? t('notifications.you') : byCustomer ? t('notifications.aCustomer') : item.actorName;
  const what = item.subjectReference ?? t('notifications.aBooking');
  const parts = { who, what };

  switch (item.kind) {
    // Raised from outside the dealership. Their rows carry no actor on purpose -- a customer's name is never copied
    // into this table -- so they do not use `who`.
    case 'BookingRequested':
      return t('notifications.customerRequested', { what });
    case 'BookingCancelledByCustomer':
      return t('notifications.customerCancelled', { what });
    case 'BookingNonDeliveryReported':
      return t('notifications.customerReportedNonDelivery', { what });
    // Opened by the customer, or by a colleague (who is never told of their own).
    case 'DisputeOpened':
      return byCustomer ? t('notifications.customerOpenedDispute', { what }) : t('notifications.openedDispute', parts);
    // What the platform did (Wave 3, C5): always Khadra, so no `who`.
    case 'DisputeResolved':
      return t('notifications.disputeResolved', { what });
    case 'BookingCompleted':
      return t('notifications.completed', { what });
    case 'BookingMarkedNoShow':
      return t('notifications.markedNoShow', { what });
    case 'BookingExpiredUnpaid':
      return t('notifications.expiredUnpaid', { what });
    case 'BookingCancelledByAdmin':
      return t('notifications.cancelledByKhadra', { what });
    // A settlement's number, never an amount: the row holds none.
    case 'SettlementRecorded':
      return t('notifications.settlementRecorded', { what: item.subjectReference ?? '' });
    case 'SettlementVoided':
      return t('notifications.settlementVoided', { what: item.subjectReference ?? '' });
    case 'BookingApproved':
      return mine ? t('notifications.approvedByYou', { what }) : t('notifications.approved', parts);
    case 'BookingRejected':
      return mine ? t('notifications.rejectedByYou', { what }) : t('notifications.rejected', parts);
    case 'BookingPickedUp':
      return mine ? t('notifications.recordedPickupByYou', { what }) : t('notifications.recordedPickup', parts);
    case 'BookingReturned':
      // Until Wave 3 the settlement sweep reported a COMPLETION this way, as a return by "A customer" (C5). A return
      // is recorded by a member of staff, so this pair is that old completion, worded as what it was.
      if (byCustomer) return t('notifications.completed', { what });
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

/** Where a row leads, for the console the reader is standing in: `/dealer` for the owner, `/employee` for staff. */
export function notificationRoute(item: NotificationItem, area: 'employee' | 'dealer'): readonly string[] | null {
  if (!item.subjectId) return null;

  switch (item.kind) {
    case 'BookingRequested':
    case 'BookingApproved':
    case 'BookingRejected':
    case 'BookingPickedUp':
    case 'BookingReturned':
    case 'BookingConfirmed':
    case 'BookingCancelledByCustomer':
    case 'BookingNonDeliveryReported':
    case 'BookingCompleted':
    case 'BookingMarkedNoShow':
    case 'BookingExpiredUnpaid':
    case 'BookingCancelledByAdmin':
      return [`/${area}/bookings`, item.subjectId];
    // The subject is the TICKET.
    case 'DisputeOpened':
    case 'DisputeResolved':
      return [`/${area}/disputes`, item.subjectId];
    // Payouts lists every settlement; only the owner and the employees granted reports are told of one.
    case 'SettlementRecorded':
    case 'SettlementVoided':
      return [`/${area}/payouts`];
    // The dealership kinds point at the dealership itself, which an employee has no screen for
    // beyond the read-only one; the row says what happened and that is the whole of it.
    default:
      return null;
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
 * Each row opens inside `root`, the console the reader works in: an owner's `/dealer`, an employee's
 * `/employee` (E2E F79: the owner's console sends an employee back to their own dashboard).
 */
export function toDealerNotifications(
  dashboard: DealerDashboard | null,
  now: number,
  t: Translate,
  localeTag: string,
  root: '/dealer' | '/employee',
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
      route: `${root}/bookings`,
    });
  }

  if (dashboard.bookings.requested > 0) {
    const n = dashboard.bookings.requested;
    const oldest = dashboard.bookings.oldestRequestedAt;
    const earliest = dashboard.bookings.earliestDecisionDeadline;
    rows.push({
      id: 'pending-requests',
      title: t('notifications.requestsWaiting', { count: n }),
      // The oldest, and when the first one expires: the server's moment (Wave 3, F24). The oldest is
      // not always the next to expire -- a newer request for a sooner rental can be -- and the line
      // used to say a request expires when its rental date arrives.
      detail:
        oldest && earliest
          ? t('notifications.oldestAndExpiry', {
              when: relativeTime(oldest, now, localeTag),
              deadline: relativeTime(earliest, now, localeTag),
            })
          : t('notifications.aRequestExpiresWhen'),
      when: t('notifications.toAnswer'),
      tone: 'warn',
      icon: 'calendar-check',
      route: `${root}/bookings`,
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
      route: `${root}/bookings/${pickup.bookingId}`,
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
      route: `${root}/bookings/${back.bookingId}`,
    });
  }

  return rows;
}
