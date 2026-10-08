import { NotificationItem } from '../../core/api/notifications.service';
import { TranslationKey } from '../../core/i18n/en';
import { I18nService } from '../../core/i18n/i18n.service';

/**
 * The customer kinds the platform sends (NotificationKind, the Your* entries). A kind a newer server
 * sends reads as a generic update, never blank.
 */
export const KNOWN_KINDS: ReadonlySet<string> = new Set([
  'YourBookingApproved',
  'YourBookingRejected',
  'YourBookingExpired',
  'YourBookingCompleted',
  'YourBookingMarkedNoShow',
  'YourBookingConfirmed',
  'YourBookingCancelled',
  'YourBookingPickedUp',
  'YourBookingReturned',
  'YourPaymentReminder',
  'YourPickupReminder',
  'YourReturnReminder',
  'YourDisputeUpdated',
  // The customer's own dispute, confirmed (Wave 3, D10). Its subject is the booking, which links the dispute.
  'YourDisputeOpened',
  'YourDepositRefunded',
  'YourPartialRefundSettled',
  // About the account, not a booking (Wave 4, W4-9): no subject, and it opens the documents page.
  'YourDocumentRejected',
]);

/** One notification's sentence, the same on the full page and in the header's panel. */
export function notificationText(i18n: I18nService, item: NotificationItem): string {
  // An office that left the platform is a code, worded here (pre-launch item 103); every other row names its office.
  const actor =
    item.actorStandIn === 'RentalOffice' ? i18n.t('notification.someone') : item.actorName || i18n.t('notification.someone');
  return KNOWN_KINDS.has(item.kind)
    ? i18n.t(`notification.${item.kind}` as TranslationKey, { actor })
    : i18n.t('notification.unknown', { actor });
}

/** "About booking KH-…" or "About dispute …", when the notification names what it is about. */
export function notificationAbout(i18n: I18nService, item: NotificationItem): string {
  if (!item.subjectReference) return '';
  const aboutDispute = item.kind === 'YourDisputeUpdated' || item.kind === 'YourDisputeOpened';
  return i18n.t(aboutDispute ? 'notifications.aboutDispute' : 'notifications.about', {
    reference: item.subjectReference,
  });
}
