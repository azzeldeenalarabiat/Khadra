import { describe, expect, it } from 'vitest';
import { AR } from '../../core/i18n/ar';
import { EN, TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';
import { ProblemSnapshot } from '../../core/i18n/problem';
import { resolveMessage } from '../../core/i18n/resolve';
import { Booking, PenaltyAssessment } from '../../core/models/bookings.api';
import { Dispute } from '../../core/models/disputes.api';
import { Money } from '../../core/models/fleet.api';
import {
  chargeAllowance,
  chargeProblem,
  disputeRefusal,
  exceedsPlaces,
  slaTone,
  splitPlaces,
  stepFor,
} from './dispute-form.presenter';

/**
 * The administrator's resolution form says before the click what the server would refuse after it
 * (E2E F34, F35, F36, F42; checklist 16). Each rule here restates one in `DisputeResolution.Create` or
 * `DisputeSlaStates`, and the refusals are resolved against the REAL dictionaries.
 */
const en = (key: TranslationKey, params?: MessageParams) =>
  resolveMessage(EN[key], params, 'en-GB', false) ?? key;
// Arabic wraps every value in zero-width direction isolates (resolve.ts); compare the visible words.
const ar = (key: TranslationKey, params?: MessageParams) =>
  (resolveMessage(AR[key], params, 'ar-JO-u-nu-latn', true) ?? key).replace(/[⁨⁩]/g, '');
const jod = (amount: number): Money => ({ amount, currency: 'JOD' });

function penalty(
  attributedTo: PenaltyAssessment['attributedTo'],
  min: number,
  max: number,
): PenaltyAssessment {
  return {
    attributedTo,
    minPercent: 0,
    maxPercent: 0,
    minAmount: jod(min),
    maxAmount: jod(max),
    isRange: min !== max,
    isNothingOwed: max === 0,
    reason: '',
    reasonCode: null,
    assessedAt: '2026-10-04T09:00:00Z',
  };
}

type ChargeFacts = Pick<Dispute, 'booking' | 'chargedToDealerEarlier'>;
const ticket = (assessed: PenaltyAssessment | null, chargedEarlier?: Money | null): ChargeFacts => ({
  booking: { penalty: assessed } as Booking,
  ...(chargedEarlier === undefined ? {} : { chargedToDealerEarlier: chargedEarlier }),
});

describe('the places a split is entered at (F35, checklist 16)', () => {
  it("follows the currency's minor units once the server has said, whatever the figures look like", () => {
    // A whole-dinar deposit arrives as 50, which used to make every input step by a whole dinar.
    expect(splitPlaces(3, 50)).toBe(3);
    expect(stepFor(splitPlaces(3, 50))).toBe(0.001);
  });

  it('falls back to the precision of the figures on screen until it has', () => {
    expect(splitPlaces(undefined, 50, 1.5)).toBe(1);
    expect(stepFor(splitPlaces(undefined, 50))).toBe(1);
  });

  it('refuses more places than the currency has, rather than rounding them (F36)', () => {
    expect(exceedsPlaces(1.2345, 3)).toBe(true);
    expect(exceedsPlaces(1.5, 3)).toBe(false);
    expect(exceedsPlaces(1.234, 3)).toBe(false);
    // Unknown until the server says: the server's own refusal is the answer then.
    expect(exceedsPlaces(1.2345, undefined)).toBe(false);
  });
});

describe('what the booking lets a ticket charge the office (F34)', () => {
  it('is nothing when the booking assessed no penalty, or one against anyone but the office', () => {
    expect(chargeAllowance(ticket(null, jod(0)))).toEqual({ kind: 'none', reason: 'notAssessed' });
    expect(chargeAllowance(ticket(penalty('Customer', 10, 25), jod(0)))).toEqual({
      kind: 'none',
      reason: 'notAssessed',
    });
    expect(chargeAllowance(ticket(penalty('System', 10, 25), jod(0)))).toEqual({
      kind: 'none',
      reason: 'notAssessed',
    });
  });

  it('is nothing when the penalty against the office came to nothing owed', () => {
    expect(chargeAllowance(ticket(penalty('Dealer', 0, 0), jod(0)))).toEqual({
      kind: 'none',
      reason: 'notAssessed',
    });
  });

  it("is the booking's whole range on a first charge", () => {
    expect(chargeAllowance(ticket(penalty('Dealer', 10, 25), jod(0)))).toEqual({
      kind: 'range',
      min: 10,
      max: 25,
      currency: 'JOD',
      chargedEarlier: null,
    });
  });

  it('is exactly the amount of a flat penalty', () => {
    expect(chargeAllowance(ticket(penalty('Dealer', 25, 25), jod(0)))).toMatchObject({
      kind: 'range',
      min: 25,
      max: 25,
    });
  });

  it('is what is left of the range after earlier disputes charged, with no floor', () => {
    expect(chargeAllowance(ticket(penalty('Dealer', 10, 25), jod(5)))).toEqual({
      kind: 'range',
      min: 0,
      max: 20,
      currency: 'JOD',
      chargedEarlier: jod(5),
    });
  });

  it('works the remainder out exactly, without binary-fraction noise', () => {
    expect(chargeAllowance(ticket(penalty('Dealer', 10, 25.5), jod(10.25)))).toMatchObject({
      max: 15.25,
    });
  });

  it('is nothing once earlier disputes charged the whole range', () => {
    expect(chargeAllowance(ticket(penalty('Dealer', 10, 25), jod(25)))).toEqual({
      kind: 'none',
      reason: 'alreadyCharged',
    });
  });

  it("is the whole range from an API too old to send the earlier charge, and the server's refusal answers", () => {
    expect(chargeAllowance(ticket(penalty('Dealer', 10, 25)))).toMatchObject({ min: 10, max: 25 });
  });
});

describe('the charge as typed', () => {
  const range = chargeAllowance(ticket(penalty('Dealer', 10, 25), jod(0)));
  const closed = chargeAllowance(ticket(null, jod(0)));

  it('is fine left empty: the charge is optional even when the booking assessed one', () => {
    expect(chargeProblem('', range, 3)).toBeNull();
    expect(chargeProblem('   ', range, 3)).toBeNull();
    expect(chargeProblem('', closed, 3)).toBeNull();
  });

  it('is fine inside the range, at its ends included', () => {
    expect(chargeProblem('10', range, 3)).toBeNull();
    expect(chargeProblem('25', range, 3)).toBeNull();
    expect(chargeProblem('12.345', range, 3)).toBeNull();
  });

  it('is refused outside the range', () => {
    expect(chargeProblem('9.999', range, 3)).toBe('outOfRange');
    expect(chargeProblem('25.001', range, 3)).toBe('outOfRange');
  });

  it('is refused with more places than the currency has, before the range is even asked', () => {
    expect(chargeProblem('12.3456', range, 3)).toBe('tooPrecise');
  });

  it('is refused when it is not an amount', () => {
    expect(chargeProblem('abc', range, 3)).toBe('notANumber');
    expect(chargeProblem('-1', range, 3)).toBe('notANumber');
  });

  it('is refused whenever the field is closed', () => {
    expect(chargeProblem('1', closed, 3)).toBe('notAllowed');
  });
});

describe("the SLA panel's tint (F42)", () => {
  it('is neutral on a ticket no longer live, whatever the clock says', () => {
    expect(slaTone({ isLive: false, slaState: 'Closed' }, true)).toBeNull();
  });

  it("follows the server's state on a live ticket: neutral on time, a warning at risk, the alarm overdue", () => {
    expect(slaTone({ isLive: true, slaState: 'OnTime' }, false)).toBeNull();
    expect(slaTone({ isLive: true, slaState: 'AtRisk' }, false)).toBe('warn');
    expect(slaTone({ isLive: true, slaState: 'Overdue' }, false)).toBe('bad');
  });

  it('is escalated by a deadline the clock has seen pass since the ticket loaded, never calmed', () => {
    expect(slaTone({ isLive: true, slaState: 'OnTime' }, true)).toBe('bad');
    expect(slaTone({ isLive: true, slaState: 'AtRisk' }, true)).toBe('bad');
  });

  it('is decided by the clock alone from an API too old to send the state', () => {
    expect(slaTone({ isLive: true }, false)).toBeNull();
    expect(slaTone({ isLive: true }, true)).toBe('bad');
  });
});

describe('a refused resolution, in both languages', () => {
  const refused = (code: string | null, title: string | null = 'Server sentence.'): ProblemSnapshot => ({
    status: 400,
    code,
    title,
    traceId: null,
    errors: null,
  });

  // Every code the resolve path can return. Each must be the console's own sentence in BOTH languages,
  // never the server's English title nor the generic "refused" line.
  const codes = [
    'dispute.disposition_unbalanced',
    'dispute.disposition_currency_mismatch',
    'dispute.amount_precision',
    'dispute.dealer_charge_unassessed',
    'dispute.dealer_charge_out_of_range',
    'dispute.dealer_charge_currency_mismatch',
    'dispute.deposit_over_allocated',
    'dispute.resolution_note_required',
    'dispute.already_resolved',
    'dispute.already_withdrawn',
    'dispute.not_found',
    'dispute.booking_missing',
    'booking.not_returned',
    'payments.not_live',
    'payments.refund_exceeds_capture',
  ];

  it.each(codes)('words %s itself', (code) => {
    const english = disputeRefusal(refused(code), en, 'en');
    const arabic = disputeRefusal(refused(code), ar, 'ar');
    expect(english).not.toBe('Server sentence.');
    expect(arabic).not.toBe(ar('common.requestRefused'));
    expect(english).not.toMatch(/^disputeDetail\./);
    expect(arabic).not.toMatch(/^disputeDetail\./);
    expect(arabic).toMatch(/[؀-ۿ]/);
  });

  it('says why a charge was refused on a booking that assessed nothing against the office (F34)', () => {
    expect(disputeRefusal(refused('dispute.dealer_charge_unassessed'), en, 'en')).toBe(
      'This booking assessed no penalty against the office, so nothing can be charged to it.',
    );
    expect(disputeRefusal(refused('dispute.dealer_charge_unassessed'), ar, 'ar')).toBe(
      'لم يقدّر هذا الحجز أي غرامة على المكتب، فلا يمكن فرض أي مبلغ عليه.',
    );
  });

  it('says an amount had more places than the currency has (F36)', () => {
    expect(disputeRefusal(refused('dispute.amount_precision'), en, 'en')).toBe(
      'One of the amounts has more decimal places than this currency has. Nothing was decided.',
    );
  });

  it("falls back to the server's sentence in English and the console's own line in Arabic", () => {
    expect(disputeRefusal(refused('dispute.something_new'), en, 'en')).toBe('Server sentence.');
    expect(disputeRefusal(refused('dispute.something_new'), ar, 'ar')).toBe(
      ar('common.requestRefused'),
    );
    expect(disputeRefusal(refused(null, null), en, 'en')).toBe(
      en('dealerDelivery.serviceDidNotRespond'),
    );
  });
});

describe('the hints under the form', () => {
  it('names the limit on places with the plural the language needs', () => {
    expect(en('disputeDetail.tooManyPlaces', { count: 3 })).toBe('At most 3 decimal places.');
    expect(ar('disputeDetail.tooManyPlaces', { count: 3 })).toBe('3 منازل عشرية كحد أقصى.');
  });
});
