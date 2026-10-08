import { describe, expect, it } from 'vitest';
import { AR } from '../../core/i18n/ar';
import { EN, TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';
import { resolveMessage } from '../../core/i18n/resolve';
import { rateLimitedMessage } from './sign-in-wait';

const en = (key: TranslationKey, params?: MessageParams) =>
  resolveMessage(EN[key], params, 'en-GB', false) ?? key;
// Arabic wraps every value in zero-width direction isolates (resolve.ts); compare the visible words.
const ar = (key: TranslationKey, params?: MessageParams) =>
  (resolveMessage(AR[key], params, 'ar-JO-u-nu-latn', true) ?? key).replace(/[\u2068\u2069]/g, '');

describe('rateLimitedMessage', () => {
  it('counts a short wait in seconds', () => {
    expect(rateLimitedMessage(en, 42)).toBe('Too many attempts. Try again in 42 seconds.');
  });

  // Pre-launch item 51: the per-account ceiling refuses a name for fifteen minutes.
  it('counts a long wait in whole minutes, rounding up', () => {
    expect(rateLimitedMessage(en, 900)).toBe('Too many attempts. Try again in 15 minutes.');
    expect(rateLimitedMessage(en, 61)).toBe('Too many attempts. Try again in 2 minutes.');
    expect(rateLimitedMessage(en, 60)).toBe('Too many attempts. Try again in 1 minute.');
    expect(rateLimitedMessage(ar, 900)).toBe('محاولات كثيرة. أعد المحاولة بعد 15 دقيقة.');
    expect(rateLimitedMessage(ar, 120)).toBe('محاولات كثيرة. أعد المحاولة بعد دقيقتين.');
  });

  it('says only "wait" when no wait was given', () => {
    expect(rateLimitedMessage(en, null)).toBe(EN['signIn.rateLimited']);
  });
});
