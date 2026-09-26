import { Dispute } from '../../core/models/disputes.api';
import { Money } from '../../core/models/fleet.api';
import { Translate } from '../dealer/renter-documents.presenter';

/**
 * What earlier disputes on a booking already decided (owner, 2026-09-26; pre-launch item 169), worded
 * for both consoles. Every figure is the SERVER's — `depositOnBooking`, `decidedByEarlierTickets` and
 * `depositHeld`, which on a later ticket is what is left — so nothing here subtracts: the only choice
 * made is which sentence, from whether the server says anything is left.
 */

/** Formats one amount with its own currency code (the console's `FormatService.money`). */
export type FormatMoney = (value: Money) => string;

/**
 * What earlier disputes decided, or null on a first dispute — and on an API older than the field,
 * which never sends it.
 */
export function decidedEarlier(d: Pick<Dispute, 'decidedByEarlierTickets'>): Money | null {
  const decided = d.decidedByEarlierTickets;
  return decided && decided.amount > 0 ? decided : null;
}

/**
 * The notice on a LIVE ticket that earlier disputes already decided part or all of the deposit, or
 * null when none did. A resolved or withdrawn ticket gets none: it is not deciding anything now.
 */
export function earlierDecisionNotice(
  d: Pick<Dispute, 'isLive' | 'depositHeld' | 'depositOnBooking' | 'decidedByEarlierTickets'>,
  t: Translate,
  money: FormatMoney,
): string | null {
  const decided = decidedEarlier(d);
  if (!d.isLive || !decided || !d.depositOnBooking) return null;
  return d.depositHeld.amount > 0
    ? t('common.earlierDisputeDecidedPart', {
        decided: money(decided),
        onBooking: money(d.depositOnBooking),
        held: money(d.depositHeld),
      })
    : t('common.earlierDisputeDecidedAll', { onBooking: money(d.depositOnBooking) });
}
