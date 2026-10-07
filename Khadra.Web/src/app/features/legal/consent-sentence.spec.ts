import { describe, expect, it } from 'vitest';
import { AR } from '../../core/i18n/ar';
import { EN, TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';
import { consentSentence, slugsInForce } from './consent-sentence';

/**
 * The sentence a customer ticks as they register (Wave 4, W4-8): it names exactly the texts in force, each linked to
 * its page here, and reads whole in both languages — Arabic joins "و" to the name that follows.
 */
const tIn =
  (dictionary: Record<string, unknown>) =>
  (key: TranslationKey, _params?: MessageParams): string =>
    String(dictionary[key] ?? key);

const read = (parts: readonly { text: string }[]): string => parts.map((part) => part.text).join('');

describe('the consent sentence', () => {
  it('names both texts in force, each linked to its page', () => {
    const parts = consentSentence(['terms', 'privacy'], tIn(EN));

    expect(read(parts)).toBe('I have read and accept the Terms of Service and the Privacy notice');
    expect(parts.filter((part) => part.slug).map((part) => part.slug)).toEqual(['terms', 'privacy']);
  });

  it('reads whole in Arabic, the conjunction joined to the second name', () => {
    expect(read(consentSentence(['terms', 'privacy'], tIn(AR)))).toBe('قرأت وأوافق على شروط الخدمة وإشعار الخصوصية');
  });

  it('is nothing at all when nothing is in force: there is nothing to accept', () => {
    expect(consentSentence([], tIn(EN))).toEqual([]);
  });

  it('names only the texts in force that this website has a page for, in its own order', () => {
    expect(slugsInForce([{ slug: 'privacy' }, { slug: 'terms' }, { slug: 'cookies' }])).toEqual(['terms', 'privacy']);
    expect(slugsInForce([{ slug: 'privacy' }])).toEqual(['privacy']);
    expect(slugsInForce([])).toEqual([]);
  });
});
