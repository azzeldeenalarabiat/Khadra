import { describe, expect, it } from 'vitest';
import { AR } from '../../core/i18n/ar';
import { EN, TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';
import { resolveMessage } from '../../core/i18n/resolve';

/**
 * Pre-launch item 208: since item 164 an assessed penalty IS kept from the deposit when the dispute
 * window closes, so no sentence may promise that nothing is charged without a dispute. The owner signed
 * these off word for word on 2026-10-09; the app carries the same sentences (`cancelPenaltyNotice`,
 * `bookTermsCancellationPenalty`). Resolved against the REAL dictionaries.
 */
const en = (key: TranslationKey, params?: MessageParams) =>
  resolveMessage(EN[key], params, 'en-GB', false) ?? key;
// Arabic wraps every value in zero-width direction isolates (resolve.ts); compare the visible words.
const ar = (key: TranslationKey, params?: MessageParams) =>
  (resolveMessage(AR[key], params, 'ar-JO-u-nu-latn', true) ?? key).replace(/[\u2068\u2069]/g, '');

describe('what a late cancellation costs (item 208, as approved)', () => {
  it('the cancellation sheet, in both languages', () => {
    expect(en('cancel.penalty', { amount: 'JOD 18.000' })).toBe(
      'Cancelling now incurs a penalty of JOD 18.000. It will be deducted from your deposit when the dispute window closes, unless the dispute outcome changes this.',
    );
    expect(ar('cancel.penalty', { amount: '18.000 JOD' })).toBe(
      'الإلغاء الآن يترتب عليه غرامة قدرها 18.000 JOD. سيتم حسمها من عربونك عند انتهاء مهلة النزاع، إلا إذا صدر قرار مختلف في النزاع.',
    );
  });

  it('the booking terms, in both languages', () => {
    expect(en('book.termsPenalty', { percent: '100%' })).toBe(
      'Cancelling after that incurs a penalty of 100% of the deposit. It will be deducted from your deposit when the dispute window closes, unless the dispute outcome changes this.',
    );
    expect(ar('book.termsPenalty', { percent: '100%' })).toBe(
      'الإلغاء بعد ذلك يترتب عليه غرامة قدرها 100% من العربون. سيتم حسمها من عربونك عند انتهاء مهلة النزاع، إلا إذا صدر قرار مختلف في النزاع.',
    );
  });

  it('neither promises that nothing is charged', () => {
    for (const key of ['cancel.penalty', 'book.termsPenalty'] as const) {
      expect(en(key, { amount: 'x', percent: 'x' })).not.toContain('Nothing is charged');
    }
  });
});
