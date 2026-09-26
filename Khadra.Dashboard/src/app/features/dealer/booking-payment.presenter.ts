import { TranslationKey } from '../../core/i18n/en';

/**
 * How the dealer console words what a customer paid (owner, 2026-09-25): a booking paid in full is
 * never called a deposit. The verdict is the server's — the confirming payment's purpose — so the
 * screen compares no amounts of its own. The Financial section's lines are `office-money.presenter`'s,
 * from the booking's financial state (payments Phase 4b).
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
