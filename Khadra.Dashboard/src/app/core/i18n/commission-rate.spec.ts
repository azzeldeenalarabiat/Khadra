import { describe, expect, it } from 'vitest';
import { AR } from './ar';
import { commissionRate } from './commission-rate';
import { EN, TranslationKey } from './en';
import { MessageParams } from './language';
import { resolveMessage } from './resolve';

/**
 * The commission rate always says what it is a percent OF (owner, 2026-09-25).
 *
 * Resolved against the REAL dictionaries, so these assertions read as the words a gallery and an
 * administrator see: "Platform commission · 20%" beside 20.000 on a 200.000 rental read as 20% of the
 * whole rental, and the fix is only real if both languages say the basis.
 */
const en = (key: TranslationKey, params?: MessageParams) => resolveMessage(EN[key], params, 'en-GB', false) ?? key;
// Arabic wraps every value in zero-width direction isolates (resolve.ts); compare the visible words.
const ar = (key: TranslationKey, params?: MessageParams) =>
  (resolveMessage(AR[key], params, 'ar-JO-u-nu-latn', true) ?? key).replace(/[\u2068\u2069]/g, '');

describe('commissionRate', () => {
  it('names one daily rate as the basis of a one-day commission', () => {
    expect(commissionRate(en, '20%', 'OneDay')).toBe('20% of one daily rate');
    expect(commissionRate(ar, '20%', 'OneDay')).toBe('20% من سعر يوم واحد');
  });

  it('names the rental total as the basis of a rental-total commission', () => {
    expect(commissionRate(en, '12.5%', 'RentalTotal')).toBe('12.5% of the rental total');
    expect(commissionRate(ar, '12.5%', 'RentalTotal')).toBe('12.5% من إجمالي الإيجار');
  });

  it('claims no basis the server did not state', () => {
    expect(commissionRate(en, '20%', undefined)).toBe('20%');
    expect(commissionRate(en, '20%', 'SomethingNew')).toBe('20%');
  });
});

describe('the commission labels built from it', () => {
  it('reads as a share of one day on the dealer booking, in both languages', () => {
    const rate = (t: typeof en) => commissionRate(t, '20%', 'OneDay');

    const english = en('dealerBooking.platformCommissionFrozen', { rate: rate(en) });
    expect(english).toBe('Platform commission · 20% of one daily rate (frozen on this booking)');
    // Never the ambiguous form the E2E found.
    expect(english).not.toMatch(/Platform commission · 20% \(/);

    expect(ar('dealerBooking.platformCommissionFrozen', { rate: rate(ar) })).toBe(
      'عمولة المنصة · 20% من سعر يوم واحد (مجمَّدة على هذا الحجز)',
    );
  });

  it('reads the same way on the administrator booking page', () => {
    expect(en('adminBooking.platformCommissionWithPercent', { rate: commissionRate(en, '20%', 'OneDay') })).toBe(
      'Platform commission (20% of one daily rate)',
    );
    expect(ar('adminBooking.platformCommissionWithPercent', { rate: commissionRate(ar, '20%', 'OneDay') })).toBe(
      'عمولة المنصة (20% من سعر يوم واحد)',
    );
  });

  it('says the commission comes out of the card payment, which is the deposit or the whole booking', () => {
    expect(EN['dealerBooking.commissionIsDeductedFrom']).toContain('card payment');
    expect(EN['dealerBooking.commissionIsDeductedFrom']).not.toContain('card deposit');
    expect(AR['dealerBooking.commissionIsDeductedFrom']).toContain('دفعة البطاقة');
  });
});
