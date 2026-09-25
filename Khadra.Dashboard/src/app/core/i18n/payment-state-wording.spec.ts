import { describe, expect, it } from 'vitest';
import { AR } from './ar';
import { EN, TranslationKey } from './en';
import { MessageParams } from './language';
import { resolveMessage } from './resolve';
import { statusKey } from './status-key';

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
