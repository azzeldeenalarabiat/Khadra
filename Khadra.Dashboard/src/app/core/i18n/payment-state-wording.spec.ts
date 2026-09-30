import { describe, expect, it } from 'vitest';
import { AR } from './ar';
import { EN, TranslationKey } from './en';
import { MessageParams } from './language';
import { resolveMessage } from './resolve';
import { statusKey } from './status-key';
import { penaltyStandingKey } from './money-words';

/**
 * Two consistency fixes the owner asked for before Phase 3 (2026-09-25), against the REAL dictionaries.
 *
 * 1. An approved booking awaits a PAYMENT, not a deposit: the customer chooses the 20% deposit or the
 *    whole amount at checkout.
 * 2. The free-cancellation window starts when a payment confirms the booking (Booking.ConfirmPayment,
 *    capped at the rental start), never at approval.
 */
const en = (key: TranslationKey, params?: MessageParams) => resolveMessage(EN[key], params, 'en-GB', false) ?? key;
// Arabic wraps every value in zero-width direction isolates (resolve.ts); compare the visible words.
const ar = (key: TranslationKey, params?: MessageParams) =>
  (resolveMessage(AR[key], params, 'ar-JO-u-nu-latn', true) ?? key).replace(/[\u2068\u2069]/g, '');

describe('the approved state in the dealer console', () => {
  it('the booking badge reads awaiting payment, in both languages', () => {
    const key = statusKey('Approved', 'dealerBooking')!;
    expect(en(key)).toBe('Awaiting payment');
    expect(ar(key)).toBe('بانتظار الدفع');
  });

  it('the dashboard tile and the vehicle schedule say the same', () => {
    expect(en('dealerDashboard.awaitingPayment')).toBe('Awaiting payment');
    expect(ar('dealerDashboard.awaitingPayment')).toBe('بانتظار الدفع');
    expect(en('status.awaitingPayment')).toBe('Awaiting payment');
    expect(ar('status.awaitingPayment')).toBe('بانتظار الدفع');
    expect(AR['dealerDashboard.approvedAndUnpaid']).not.toContain('عربون');
  });
});

describe('the free-cancellation window', () => {
  it('starts at payment, in hours, in both languages', () => {
    expect(en('freeCancellation.withinHoursOfPayment', { count: 1 })).toBe('Within 1 hour after payment');
    expect(en('freeCancellation.withinHoursOfPayment', { count: 2 })).toBe('Within 2 hours after payment');
    expect(ar('freeCancellation.withinHoursOfPayment', { count: 1 })).toBe('خلال ساعة واحدة من وقت الدفع');
    expect(ar('freeCancellation.withinHoursOfPayment', { count: 2 })).toBe('خلال ساعتين من وقت الدفع');
    expect(ar('freeCancellation.withinHoursOfPayment', { count: 3 })).toBe('خلال 3 ساعات من وقت الدفع');
  });

  it('starts at payment on the settings screen too, in minutes', () => {
    expect(en('freeCancellation.withinMinutesOfPayment', { count: 60 })).toBe('Within 60 minutes after payment');
    expect(ar('freeCancellation.withinMinutesOfPayment', { count: 60 })).toBe('خلال 60 دقيقة من وقت الدفع');
  });

  it('no console text measures it from approval any more', () => {
    for (const [key, message] of Object.entries(EN)) {
      const text = JSON.stringify(message);
      if (/free cancel/i.test(key + text) || key.startsWith('freeCancellation.')) {
        expect(text).not.toMatch(/after approval/i);
      }
    }
    expect(JSON.stringify(Object.values(AR))).not.toContain('ساعة بعد الموافقة');
  });
});

/**
 * Payments Phase 8 (owner, 2026-09-29): a customer's penalty of the whole deposit is KEPT when the dispute window
 * closes with no dispute, so no console may go on saying a penalty becomes money only through a dispute — for that
 * penalty, or once it has been kept.
 */
describe('where an assessed penalty stands, in both consoles', () => {
  it('reads the server state and flag, and keeps the old sentence only for a penalty that still needs a dispute', () => {
    expect(penaltyStandingKey({ state: 'KeptFromDeposit', requiresTicketToEnforce: false }, 'admin')).toBe('penaltyStanding.keptFromDeposit');
    expect(penaltyStandingKey({ state: 'ResolvedByDispute', requiresTicketToEnforce: false }, 'office')).toBe('penaltyStanding.resolvedByDispute');
    expect(penaltyStandingKey({ state: 'Assessed', requiresTicketToEnforce: false }, 'admin')).toBe('penaltyStanding.keptUnlessDisputed');
    expect(penaltyStandingKey({ state: 'Assessed', requiresTicketToEnforce: true }, 'admin')).toBe('adminBooking.assessedNotChargedMoney');
    expect(penaltyStandingKey({ state: 'Assessed', requiresTicketToEnforce: true }, 'office')).toBe('dealerBooking.assessedNotChargedMoney');
    // An older API sends neither: the old sentence, which was true for it.
    expect(penaltyStandingKey({}, 'office')).toBe('dealerBooking.assessedNotChargedMoney');
  });

  it('never says "nothing is charged" where the kept penalty could make it false', () => {
    expect(en('penaltyStanding.keptFromDeposit')).toBe('Kept from the deposit: the dispute window closed with no dispute.');
    expect(ar('penaltyStanding.keptFromDeposit')).toBe('احتُفظ بها من العربون: انتهت مهلة النزاع دون فتح نزاع.');
    for (const key of ['dealerDispute.settlesAsIfNone', 'dealerDispute.theAmicablePathThe', 'disputeDetail.thisTicketWasWithdrawn', 'dealerBooking.thePlatformAnswersWithin'] as const) {
      expect(en(key), key).not.toMatch(/nothing is charged/i);
      expect(ar(key), key).not.toContain('لا يُحصَّل شيء من أحد');
    }
    expect(en('adminBooking.noShowBody', { customer: 'Rana' })).toContain('kept from the deposit only when the dispute window closes with no dispute');
  });
});

describe("the office's booking money, once payouts are live", () => {
  it('no longer says the net payout is not shown', () => {
    expect(en('dealerBooking.commissionIsDeductedFrom')).not.toMatch(/not live|no net figure/i);
    expect(en('dealerBooking.commissionIsDeductedFrom')).toContain('Payouts page');
    expect(ar('dealerBooking.commissionIsDeductedFrom')).not.toContain('لم تُفعَّل بعد');
    expect(ar('dealerBooking.commissionIsDeductedFrom')).toContain('صفحة التحويلات');
  });
});
