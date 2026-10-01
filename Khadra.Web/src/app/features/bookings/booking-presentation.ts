import { TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';
import { CountdownParts, countdownUnits } from './countdown';

type Translate = (key: TranslationKey, params?: MessageParams) => string;

/** The platform's lifecycle, in order. A booking that ended early shows only the stages it reached. */
export const LIFECYCLE = ['Requested', 'Approved', 'Confirmed', 'PickedUp', 'Returned', 'Completed'] as const;

const KNOWN_STATUSES = new Set([
  'Requested',
  'Approved',
  'Confirmed',
  'PickedUp',
  'Returned',
  'Completed',
  'Cancelled',
  'Rejected',
  'NoShow',
  'Expired',
]);

/** Colour class for a status, the app's scheme: waiting amber, good green, ended red, closed grey. */
export function statusTone(status: string): 'badge--warn' | 'badge--ok' | 'badge--bad' | '' {
  switch (status) {
    case 'Requested':
    case 'Approved':
      return 'badge--warn';
    case 'Confirmed':
    case 'PickedUp':
    case 'Completed':
      return 'badge--ok';
    case 'Cancelled':
    case 'Rejected':
    case 'NoShow':
      return 'badge--bad';
    default:
      return '';
  }
}

/** A status the site knows words for; an unknown one (a newer server) shows its name rather than nothing. */
export function statusLabel(t: Translate, status: string): string {
  return KNOWN_STATUSES.has(status) ? t(`status.${status}` as TranslationKey) : status;
}

/**
 * A stage in the past tense. The Confirmed stage names the payment that reached it (owner,
 * 2026-09-25): "Deposit paid" was wrong for a booking paid in full. `confirmedBy` is the confirming
 * payment's purpose, from the server; anything else keeps the deposit wording.
 */
export function stageLabel(t: Translate, status: string, confirmedBy?: string | null): string {
  if (status === 'Confirmed' && confirmedBy === 'FullPayment') return t('booking.stage.ConfirmedPaidInFull');
  return KNOWN_STATUSES.has(status) ? t(`booking.stage.${status}` as TranslationKey) : status;
}

export function partyLabel(t: Translate, party: string | null | undefined): string {
  switch (party) {
    case 'Customer':
    case 'Dealer':
    case 'Admin':
    case 'System':
      return t(`booking.party.${party}` as TranslationKey);
    default:
      return party ?? '';
  }
}

/** Who made a change, as the history says it: the reader's own act in a phrase of its own (pre-launch item 218). */
export function byWhom(t: Translate, party: string | null | undefined): string {
  return party === 'Customer' ? t('booking.byYou') : t('booking.by', { party: partyLabel(t, party) });
}

export function countdownText(t: Translate, parts: CountdownParts | null): string {
  if (!parts) return '';
  if (parts.over) return t('countdown.over');
  const [first, second] = countdownUnits(parts).map(([unit, count]) => t(`countdown.${unit}` as TranslationKey, { count }));
  const time = second ? t('countdown.pair', { first: first!, second }) : first!;
  return t('countdown.left', { time });
}
