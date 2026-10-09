import { describe, expect, it } from 'vitest';
import { Money } from '../../core/api/common.api';
import { AR } from '../../core/i18n/ar';
import { EN, TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';
import { resolveMessage } from '../../core/i18n/resolve';
import {
  decidedEarlier,
  earlierDecisionNotice,
  EarlierDecisionFigures,
} from './earlier-decisions';

/**
 * A later dispute on a booking splits only what earlier ones left (owner, 2026-09-26; item 169). The
 * website says so in the server's own figures, the same sentence the app and both consoles use —
 * resolved against the REAL dictionaries, so these are the words a customer reads.
 */
const en = (key: TranslationKey, params?: MessageParams) =>
  resolveMessage(EN[key], params, 'en-GB', false) ?? key;
// Arabic wraps every value in zero-width direction isolates (resolve.ts); compare the visible words.
const ar = (key: TranslationKey, params?: MessageParams) =>
  (resolveMessage(AR[key], params, 'ar-JO-u-nu-latn', true) ?? key).replace(/[\u2068\u2069]/g, '');
const money = (value: Money) => `${value.amount} ${value.currency}`;
const jod = (amount: number): Money => ({ amount, currency: 'JOD' });
const live = (held: number, decided: number): EarlierDecisionFigures => ({
  isLive: true,
  depositHeld: jod(held),
  depositOnBooking: jod(18),
  decidedByEarlierTickets: jod(decided),
});

/**
 * A settled dispute is shown to the customer in their own figures, beside what earlier disputes decided
 * (owner decision 3; pre-launch item 151). The page no longer sums a decision up as "nothing is owed by
 * either side": the office's share and any charge to it are not the customer's, and are not sent to them.
 */
describe('what earlier disputes decided, beside a settled one', () => {
  it('is the server figure when one decided part of the deposit', () => {
    expect(decidedEarlier({ decidedByEarlierTickets: jod(18) })).toEqual(jod(18));
    expect(en('dispute.decidedEarlier')).toBe('Decided by earlier disputes');
    expect(ar('dispute.decidedEarlier')).toBe('حُسم في نزاعات سابقة');
  });

  it('is nothing on a first dispute, and from an API too old to say', () => {
    expect(decidedEarlier({ decidedByEarlierTickets: jod(0) })).toBeNull();
    expect(decidedEarlier({})).toBeNull();
  });

  it('leaves no wording behind that sums a decision up for the other parties', () => {
    expect('dispute.waived' in EN).toBe(false);
    expect('dispute.dealerCharge' in EN).toBe(false);
    expect('dispute.waived' in AR).toBe(false);
    expect('dispute.dealerCharge' in AR).toBe(false);
  });
});

describe('the notice on a live dispute', () => {
  it('names what is left after a partial decision, in both languages', () => {
    expect(earlierDecisionNotice(live(13, 5), en, money)).toBe(
      'An earlier dispute on this booking already decided 5 JOD of its 18 JOD deposit, so this one can decide only what is left: 13 JOD.',
    );
    expect(earlierDecisionNotice(live(13, 5), ar, money)).toBe(
      'قرّر نزاع سابق على هذا الحجز مصير 5 JOD من عربونه البالغ 18 JOD، فلا يملك هذا النزاع إلا المتبقي منه: 13 JOD.',
    );
  });

  it('says nothing is left once the whole deposit was decided, in both languages', () => {
    expect(earlierDecisionNotice(live(0, 18), en, money)).toBe(
      'An earlier dispute on this booking already decided its whole 18 JOD deposit, so this one has nothing left to split.',
    );
    expect(earlierDecisionNotice(live(0, 18), ar, money)).toBe(
      'قرّر نزاع سابق على هذا الحجز مصير عربونه كاملًا البالغ 18 JOD، فلم يبقَ منه ما يوزّعه هذا النزاع.',
    );
  });

  it('is absent on a first dispute, on a closed one, and from an API too old to say', () => {
    expect(earlierDecisionNotice(live(18, 0), en, money)).toBeNull();
    expect(earlierDecisionNotice({ ...live(0, 18), isLive: false }, en, money)).toBeNull();
    expect(earlierDecisionNotice({ isLive: true, depositHeld: jod(18) }, en, money)).toBeNull();
  });
});
