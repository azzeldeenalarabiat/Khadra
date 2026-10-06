import { TranslationKey } from '../../core/i18n/en';
import { StatusScope } from '../../core/i18n/status-key';
import { DealerActivityEntry } from '../../core/models/dealer-console.api';
import { Translate } from '../../core/services/dashboard.presenter';
import { IconName } from '../../shared/icon/icon-paths';

/**
 * A change on one of the office's bookings, worded for the office (Wave 3: E2E F23 and F27). One set of words for the
 * Activity screen, the dashboard's recent activity, a car's log and the booking page's history, so the four cannot
 * call one change two things.
 *
 * Every change is HISTORY: it says what happened, never what the booking waits for now. "Requested · awaiting your
 * answer" stayed on the booking page after the office had answered (F23). Only the booking page knows whether its
 * latest step is still waiting, and it words that step itself.
 *
 * Activity lists every change now, whoever made it (F27): the customer's, the platform's and Khadra's as well as the
 * office's. A person is named only on the office's own; the customer is "the customer" and the platform is "Khadra".
 */

export interface ActivityWords {
  readonly t: Translate;
  /** `I18nService.statusLabel`. */
  readonly status: (name: string, scope?: StatusScope) => string;
  /** `I18nService.enumLabel('party', …)`, for a party this build has no word for. */
  readonly party: (name: string) => string;
}

/** What these words read from a change: an Activity entry, or a step of a booking's own history. */
export type ActivityChange = Pick<
  DealerActivityEntry,
  'toStatus' | 'fromStatus' | 'actorParty' | 'reason'
>;

/**
 * The changes worded as more than a status's name. Every other status is the office's own word for it, so a status
 * the domain adds later still reads as something rather than as its identifier.
 */
const EVENTS: Readonly<Record<string, TranslationKey>> = {
  // Neutral: an entry carries no payment, and a booking can be paid by its deposit or in full.
  Confirmed: 'dealerActivity.paymentReceivedBookingConfirmed',
  // The handover itself, not the queue's word for the rental it starts ("Active").
  PickedUp: 'status.pickedUp',
  NoShow: 'dealerActivity.markedNoShow',
};

/** "Requested", "Approved", "Expired unpaid": what happened, in the past tense. */
export function activityEvent(change: ActivityChange, words: ActivityWords): string {
  switch (change.toStatus) {
    // The status's plain name, not the queue's ("Pending", "Awaiting payment"): those say what is waiting NOW.
    case 'Requested':
    case 'Approved':
      return words.status(change.toStatus, 'booking');
    // A request nobody answered and an approval nobody paid for both end Expired; which one it was is the news.
    case 'Expired':
      if (change.fromStatus === 'Approved') return words.t('dealerActivity.expiredUnpaid');
      if (change.fromStatus === 'Requested') return words.t('dealerActivity.expiredUnanswered');
      break;
  }
  const key = EVENTS[change.toStatus];
  return key ? words.t(key) : words.status(change.toStatus, 'dealerBooking');
}

/**
 * Who made the change. On the office's own, the member of staff, or the rental office when nobody signed it, or a
 * former member of staff when the account no longer resolves; otherwise the customer, or Khadra for anything the
 * platform or an administrator did. The server sends no id or name for those, deliberately.
 */
export function activityActor(
  change: ActivityChange & Pick<DealerActivityEntry, 'actorUserId' | 'actorName'>,
  words: ActivityWords,
): string {
  switch (change.actorParty) {
    case 'Dealer':
      if (change.actorUserId === null) return words.t('common.theRentalOffice');
      return change.actorName ?? words.t('common.formerStaffMember');
    case 'Customer':
      return words.t('dealerActivity.actor.customer');
    case 'Admin':
    case 'System':
      return words.t('dealerActivity.actor.khadra');
    default:
      return words.party(change.actorParty);
  }
}

/**
 * The reason as somebody typed it, quoted on screen and never translated: the office's note or refusal, the
 * customer's own words, Khadra's reason for a cancellation (shown to both parties). An expiry, a no-show or a
 * completion carries the platform's English, which is not shown (as on the website, Wave 3 E1).
 */
export function activityReason(change: ActivityChange): string | null {
  if (!change.reason) return null;
  if (change.actorParty === 'Dealer' || change.actorParty === 'Customer') return change.reason;
  return change.actorParty === 'Admin' && change.toStatus === 'Cancelled' ? change.reason : null;
}

/**
 * One line of the dashboard's recent activity, a whole sentence: Arabic puts the actor and the booking where English
 * does not, and names them with «من قِبل», which agrees with anyone (checklist 220). What the platform's clock did
 * names no actor.
 */
export function activitySentence(entry: DealerActivityEntry, words: ActivityWords): string {
  const reference = entry.reference;
  const actor = activityActor(entry, words);
  switch (entry.toStatus) {
    case 'Requested':
      return words.t('dealerDash.activityRequested', { actor, reference });
    case 'Approved':
      return words.t('dealerDash.activityApproved', { actor, reference });
    case 'Rejected':
      return words.t('dealerDash.activityRejected', { actor, reference });
    case 'Confirmed':
      return words.t('dealerDash.activityPaid', { actor, reference });
    case 'PickedUp':
      return words.t('dealerDash.activityHandedOver', { actor, reference });
    case 'Returned':
      return words.t('dealerDash.activityTookBack', { actor, reference });
    case 'Cancelled':
      return words.t('dealerDash.activityCancelled', { actor, reference });
    case 'Expired':
      if (entry.fromStatus === 'Approved')
        return words.t('dealerDash.activityExpiredUnpaid', { reference });
      if (entry.fromStatus === 'Requested')
        return words.t('dealerDash.activityExpiredUnanswered', { reference });
      break;
    case 'NoShow':
      return words.t('dealerDash.activityNoShow', { reference });
    case 'Completed':
      return words.t('dealerDash.activityCompleted', { reference });
  }
  return words.t('dealerDash.activityOther', {
    actor,
    reference,
    status: words.status(entry.toStatus, 'dealerBooking'),
  });
}

/** The mark beside a change: an ending that went wrong, one that went right, or a handover. */
export function activityIcon(change: Pick<ActivityChange, 'toStatus'>): IconName {
  switch (change.toStatus) {
    case 'Rejected':
    case 'Cancelled':
    case 'Expired':
    case 'NoShow':
      return 'x-circle';
    case 'Approved':
    case 'Confirmed':
    case 'Completed':
      return 'check-circle';
    case 'Requested':
      return 'bell-ringing';
    default:
      return 'key';
  }
}
