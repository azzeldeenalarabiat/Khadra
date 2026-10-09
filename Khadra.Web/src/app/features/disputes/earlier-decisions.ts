import { Money } from '../../core/api/common.api';
import { TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';

/** The figures the notice reads. All three are the server's; `depositHeld` is what THIS ticket can split. */
export interface EarlierDecisionFigures {
  readonly isLive: boolean;
  readonly depositHeld?: Money;
  readonly depositOnBooking?: Money;
  readonly decidedByEarlierTickets?: Money;
}

/** What earlier disputes on the booking decided, or null on a first dispute and from an API too old to say. */
export function decidedEarlier(d: { readonly decidedByEarlierTickets?: Money }): Money | null {
  const decided = d.decidedByEarlierTickets;
  return decided && decided.amount > 0 ? decided : null;
}

/**
 * On a live dispute, what earlier disputes on the same booking already decided (owner, 2026-09-26;
 * pre-launch item 169), or null when none did — including on an API too old to say. Nothing here
 * subtracts: the only choice made is which sentence, from whether the server says anything is left.
 */
export function earlierDecisionNotice(
  d: EarlierDecisionFigures,
  t: (key: TranslationKey, params?: MessageParams) => string,
  money: (value: Money) => string,
): string | null {
  const decided = d.decidedByEarlierTickets;
  if (!d.isLive || !decided || decided.amount <= 0 || !d.depositOnBooking || !d.depositHeld)
    return null;
  return d.depositHeld.amount > 0
    ? t('dispute.earlierDecidedPart', {
        decided: money(decided),
        onBooking: money(d.depositOnBooking),
        held: money(d.depositHeld),
      })
    : t('dispute.earlierDecidedAll', { onBooking: money(d.depositOnBooking) });
}
