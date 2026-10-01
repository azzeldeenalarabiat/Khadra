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
