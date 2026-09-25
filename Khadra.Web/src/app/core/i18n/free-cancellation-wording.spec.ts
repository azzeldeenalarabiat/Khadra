import { describe, expect, it } from 'vitest';
import { AR } from './ar';
import { EN, TranslationKey } from './en';
import { MessageParams } from './language';
import { resolveMessage } from './resolve';

/**
 * The free-cancellation window a customer agrees to before booking (owner, 2026-09-25): it starts
 * when a payment confirms the booking — the deposit or the whole amount — and never runs past the
 * rental start (Booking.ConfirmPayment). It used to say "of paying the deposit", which was wrong for
 * a booking paid in full.
 */
const en = (key: TranslationKey, params?: MessageParams) => resolveMessage(EN[key], params, 'en-GB', false) ?? key;
// Arabic wraps every value in zero-width direction isolates (resolve.ts); compare the visible words.
const ar = (key: TranslationKey, params?: MessageParams) =>
  (resolveMessage(AR[key], params, 'ar-JO-u-nu-latn', true) ?? key).replace(/[\u2068\u2069]/g, '');

describe('the free-cancellation term', () => {
  it('starts at payment and ends by the rental start, in English', () => {
    expect(en('book.termsFreeCancel', { count: 1 })).toBe(
      'Cancelling is free within 1 hour after payment, as long as the rental has not started.',
    );
    expect(en('book.termsFreeCancel', { count: 2 })).toContain('within 2 hours after payment');
  });

  it('starts at payment and ends by the rental start, in Arabic', () => {
    expect(ar('book.termsFreeCancel', { count: 1 })).toBe('الإلغاء مجاني خلال ساعة واحدة من وقت الدفع، ما دام الإيجار لم يبدأ.');
    expect(ar('book.termsFreeCancel', { count: 2 })).toContain('خلال ساعتين من وقت الدفع');
  });

  it('never says the window is measured from the deposit or from approval', () => {
    const english = JSON.stringify(EN['book.termsFreeCancel']);
    const arabic = JSON.stringify(AR['book.termsFreeCancel']);
    expect(english).not.toMatch(/deposit|approval/i);
    expect(arabic).not.toContain('العربون');
    expect(arabic).not.toContain('الموافقة');
  });
});
