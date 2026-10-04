import { TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';

type Translate = (key: TranslationKey, params?: MessageParams) => string;

const PARTIES = ['Customer', 'Dealer', 'Admin'];

/** A party to a dispute as the customer reads it — you, the rental office, Khadra — and anything newer as it came. */
export function disputeParty(t: Translate, party: string): string {
  return PARTIES.includes(party) ? t(`dispute.party.${party}` as TranslationKey) : party;
}

/**
 * Who opened a dispute, and when. The customer's own has a sentence of its own, because Arabic says it was «فُتح من
 * قِبلك», never «فتحه أنت»: the party word cannot simply be dropped into the sentence for the reader (pre-launch
 * item 218).
 */
export function openedByText(t: Translate, party: string, date: string): string {
  return party === 'Customer'
    ? t('dispute.openedByYou', { date })
    : t('dispute.openedBy', { party: disputeParty(t, party), date });
}

/** A refund as the dispute page reads it from the customer's copy of the booking. */
export interface DisputeRefund {
  readonly reason: string;
  readonly amount: { readonly amount: number; readonly currency: string };
  readonly status: string;
  readonly requestedAt: string;
  readonly sentAt: string | null;
  readonly settledAt: string | null;
  readonly disputeTicketId: string | null;
}

/**
 * What became of the customer's refund from this dispute's decision, in their words — or null when there is
 * none to speak of.
 *
 * The page used to say, whatever had happened, that the amounts shown were "not a payment that has already been
 * made to you" — written before payments existed, and false the moment the refund settled (E2E F43). The refund is
 * read from the booking the dispute arrives with: the one whose `disputeTicketId` is this ticket's. A status this
 * build does not know is left unsaid rather than guessed.
 */
export function disputeRefundText(
  t: Translate,
  ticketId: string,
  refunds: readonly DisputeRefund[] | null | undefined,
  money: (value: DisputeRefund['amount']) => string,
  date: (iso: string) => string,
): string | null {
  const refund = (refunds ?? []).find((candidate) => candidate.disputeTicketId === ticketId);
  if (!refund) return null;
  const amount = money(refund.amount);
  switch (refund.status) {
    case 'Settled':
      return t('dispute.refundSettled', { amount, date: date(refund.settledAt ?? refund.requestedAt) });
    case 'Sent':
      return t('dispute.refundOnItsWay', { amount, date: date(refund.sentAt ?? refund.requestedAt) });
    case 'Requested':
      return t('dispute.refundRequested', { amount });
    case 'Failed':
      return t('dispute.refundDelayed', { amount });
    default:
      return null;
  }
}
