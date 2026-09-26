import { describe, expect, it } from 'vitest';
import { AR } from '../../core/i18n/ar';
import { EN, TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';
import { resolveMessage } from '../../core/i18n/resolve';
import { Dispute } from '../../core/models/disputes.api';
import { Money } from '../../core/models/fleet.api';
import { decidedEarlier, earlierDecisionNotice } from './earlier-decisions.presenter';

/**
 * A later dispute on a booking splits only what earlier ones left (owner, 2026-09-26; item 169), and
 * the console says so in the server's own figures — resolved against the REAL dictionaries, so these
 * read as the words an administrator or an office sees, in both languages.
 */
const en = (key: TranslationKey, params?: MessageParams) => resolveMessage(EN[key], params, 'en-GB', false) ?? key;
// Arabic wraps every value in zero-width direction isolates (resolve.ts); compare the visible words.
const ar = (key: TranslationKey, params?: MessageParams) =>
  (resolveMessage(AR[key], params, 'ar-JO-u-nu-latn', true) ?? key).replace(/[\u2068\u2069]/g, '');
const money = (value: Money) => `${value.amount} ${value.currency}`;
const jod = (amount: number): Money => ({ amount, currency: 'JOD' });

type Figures = Pick<Dispute, 'isLive' | 'depositHeld' | 'depositOnBooking' | 'decidedByEarlierTickets'>;
const live = (held: number, decided: number): Figures => ({
  isLive: true,
  depositHeld: jod(held),
  depositOnBooking: jod(18),
  decidedByEarlierTickets: jod(decided),
});

describe('what earlier disputes decided', () => {
  it('is nothing on a first dispute, and on an API too old to say', () => {
    expect(decidedEarlier({ decidedByEarlierTickets: jod(0) })).toBeNull();
    expect(decidedEarlier({})).toBeNull();
    expect(earlierDecisionNotice(live(18, 0), en, money)).toBeNull();
    expect(earlierDecisionNotice({ isLive: true, depositHeld: jod(18) }, en, money)).toBeNull();
  });

  it('is the server figure itself, never one the console worked out', () => {
    expect(decidedEarlier({ decidedByEarlierTickets: jod(5) })).toEqual(jod(5));
  });
});

describe('the notice on a live ticket', () => {
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

  it('is not shown once the ticket is closed: it is deciding nothing now', () => {
    expect(earlierDecisionNotice({ ...live(0, 18), isLive: false }, en, money)).toBeNull();
  });
});

describe('the refusals an administrator can meet on a later dispute', () => {
  it('words the office charge and the over-allocation in both languages', () => {
    expect(en('disputeDetail.chargeOutsideRange')).toContain('counting what earlier disputes on it already charged');
    expect(ar('disputeDetail.chargeOutsideRange')).toContain('نزاعات سابقة');
    expect(en('disputeDetail.depositOverAllocated')).toContain('more than its deposit');
    expect(ar('disputeDetail.depositOverAllocated')).toContain('أكثر من عربونه');
    expect(en('common.decidedByEarlierDisputes')).toBe('Decided by earlier disputes');
    expect(ar('common.decidedByEarlierDisputes')).toBe('حُسم في نزاعات سابقة');
  });
});
