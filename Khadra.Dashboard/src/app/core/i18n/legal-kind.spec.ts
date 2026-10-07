import { describe, expect, it } from 'vitest';
import { AR } from './ar';
import { EN, TranslationKey } from './en';
import { Message, MessageParams } from './language';
import { consentSentence } from './legal-kind';
import { resolveMessage } from './resolve';

/**
 * The sentence somebody ticks as they join (Wave 4, W4-8): it names exactly the texts in force, each linked to the page
 * the API named, and reads whole in both languages — Arabic joins "و" to the name that follows.
 */
const tIn =
  (dictionary: Readonly<Record<TranslationKey, Message>>, locale: string) =>
  (key: TranslationKey, params?: MessageParams): string =>
    resolveMessage(dictionary[key], params, locale, false) ?? key;

const read = (parts: { text: string }[]): string => parts.map((part) => part.text).join('');

describe('the consent sentence', () => {
  it('names both texts, each linked to its page', () => {
    const parts = consentSentence(
      [
        { kind: 'Terms', url: 'https://khadra.test/en/terms' },
        { kind: 'Privacy', url: 'https://khadra.test/en/privacy' },
      ],
      tIn(EN, 'en-GB'),
    );

    expect(read(parts)).toBe('I have read and accept the Terms of Service and the Privacy notice');
    expect(parts.filter((part) => part.url).map((part) => part.url)).toEqual([
      'https://khadra.test/en/terms',
      'https://khadra.test/en/privacy',
    ]);
  });

  it('reads whole in Arabic, the conjunction joined to the second name', () => {
    const parts = consentSentence(
      [
        { kind: 'Terms', url: 'https://khadra.test/ar/terms' },
        { kind: 'Privacy', url: 'https://khadra.test/ar/privacy' },
      ],
      tIn(AR, 'ar-JO'),
    );

    expect(read(parts)).toBe('قرأت وأوافق على شروط الخدمة وإشعار الخصوصية');
  });

  it('names only what is in force, and names a text whose page is unknown without inventing a link', () => {
    const parts = consentSentence([{ kind: 'Privacy', url: null }], tIn(EN, 'en-GB'));

    expect(read(parts)).toBe('I have read and accept the Privacy notice');
    expect(parts.every((part) => part.url === null)).toBe(true);
  });

  it('is nothing at all when nothing is in force: there is nothing to accept', () => {
    expect(consentSentence([], tIn(EN, 'en-GB'))).toEqual([]);
  });
});
