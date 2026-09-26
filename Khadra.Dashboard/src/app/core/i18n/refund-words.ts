import { Refund } from '../models/bookings.api';
import { TranslationKey } from './en';
import { MessageParams } from './language';

type Translate = (key: TranslationKey, params?: MessageParams) => string;

/**
 * A refund in the words both consoles use (Phase 3, 2026-09-26): WHY it is owed and WHERE it is, the
 * same on the dealer's booking page and the administrator's. The amount is the refund's own, with its
 * own currency; nothing here adds or compares money.
 */

/** The refund reasons the consoles word; one a newer server adds reads as a plain "Refund". */
const REFUND_REASONS: ReadonlySet<string> = new Set([
  'FreeCancellation',
  'PlatformCancellation',
  'EndedBeforePickup',
  'DisputeWindowClosed',
  'DisputeResolution',
  'OrphanedCapture',
]);

/** Why a refund is owed. */
export function refundReasonKey(reason: string): TranslationKey {
  return (REFUND_REASONS.has(reason) ? `booking.refundReason.${reason}` : 'booking.refundReason.other') as TranslationKey;
}

/** Where a refund is: Requested and Sent are on their way, Settled is back, Failed is owed and retried. */
export function refundStatusKey(refund: Pick<Refund, 'status'>): TranslationKey {
  return refund.status === 'Settled'
    ? 'booking.refundedTo'
    : refund.status === 'Failed'
      ? 'booking.refundDelayedTo'
      : 'booking.refundInitiatedTo';
}

/** One line per refund: its reason, and where it is with its amount. */
export function refundLines(
  t: Translate,
  refunds: readonly Refund[] | undefined,
  money: (value: Refund['amount']) => string,
): { readonly k: string; readonly v: string }[] {
  return (refunds ?? []).map((refund) => ({
    k: t(refundReasonKey(refund.reason)),
    v: t(refundStatusKey(refund), { amount: money(refund.amount) }),
  }));
}
