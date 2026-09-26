import { TranslationKey } from '../../core/i18n/en';
import { Booking } from '../../core/models/bookings.api';
import { Translate } from './renter-documents.presenter';

/**
 * How the dealer console words what a customer paid (owner, 2026-09-25): a booking paid in full is
 * never called a deposit. Both verdicts are the server's — the confirming payment's purpose and the
 * booking's `isPaidInFull` — so the screen compares no amounts of its own.
 */

/**
 * The Confirmed step on a booking's timeline, named after the payment that reached it. Anything but
 * a full payment, including a server too old to say, keeps the deposit wording.
 */
export function confirmedStepKey(confirmedBy: string | null | undefined): TranslationKey {
  return confirmedBy === 'FullPayment'
    ? 'dealerBooking.paidInFullBookingConfirmed'
    : 'dealerBooking.depositPaidBookingConfirmed';
}

/**
 * The label of the refund row a free cancellation leaves: the payment it returned, which is the whole
 * booking when it was paid in full, or the deposit.
 */
export function refundRowKey(booking: Pick<Booking, 'isPaidInFull'>): TranslationKey {
  return booking.isPaidInFull ? 'dealerBooking.payment' : 'common.deposit';
}

/** Refunds that return or decide the DEPOSIT: once one exists, the deposit is no longer held. */
const DEPOSIT_DECIDED: ReadonlySet<string> = new Set([
  'FreeCancellation',
  'PlatformCancellation',
  'DisputeWindowClosed',
  'DisputeResolution',
]);

/**
 * Whether the deposit is still HELD pending settlement (Phase 3): it was paid, and no refund has
 * returned it (a free cancellation's, an administrator's, the clean-close release) or decided it (a
 * dispute's). Only then does the console say "held pending settlement".
 */
export function depositStillHeld(booking: Pick<Booking, 'depositPaid' | 'refunds'>): boolean {
  return booking.depositPaid && !(booking.refunds ?? []).some((refund) => DEPOSIT_DECIDED.has(refund.reason));
}

/**
 * The money line for what the customer paid by card: the whole booking, or the deposit at its frozen
 * rate. `percent` is the deposit percent, already formatted.
 */
export function paidByCardLabel(t: Translate, booking: Pick<Booking, 'isPaidInFull'>, percent: string): string {
  return booking.isPaidInFull
    ? t('dealerBooking.paidInFullByCard')
    : t('dealerBooking.depositPaidByCard', { percent });
}
