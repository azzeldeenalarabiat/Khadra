import { describe, expect, it } from 'vitest';
import { AR } from './ar';
import { EN } from './en';
import { Message } from './language';

/**
 * The dictionaries, checked against each other and against Arabic's own grammar.
 *
 * `ar.ts` is typed `Record<TranslationKey, Message>`, so a MISSING key already fails the build. That
 * leaves the failures a type cannot see: a key in Arabic that no longer exists in English, a
 * placeholder that was dropped in translation — which silently prints a sentence with a hole in it —
 * and a plural that was translated as though Arabic had two forms like English.
 */

const placeholders = (message: Message): Set<string> => {
  const forms = typeof message === 'string' ? [message] : Object.values(message);
  const found = new Set<string>();
  for (const form of forms) {
    for (const match of (form ?? '').matchAll(/\{(\w+)\}/g)) found.add(match[1]);
  }
  return found;
};

/** Entries that are meant to read identically in both: the brand, and each language's own name. */
const UNTRANSLATED_BY_DESIGN = new Set<string>(['lang.en', 'lang.ar', 'app.name']);

describe('translation dictionaries', () => {
  it('carry exactly the same keys', () => {
    // The type catches Arabic missing an English key. This catches the other direction: a key
    // deleted from en.ts leaves dead Arabic behind, which nobody notices because nothing renders it.
    expect(Object.keys(AR).sort()).toEqual(Object.keys(EN).sort());
  });

  it('never leave an Arabic entry as the English text', () => {
    // A key whose Arabic is byte-identical to its English is almost always one that was pasted and
    // not yet translated. The genuine exceptions are the two language names, which are each written
    // in their own script on purpose.
    const untranslated = (Object.keys(EN) as (keyof typeof EN)[])
      .filter((key) => !UNTRANSLATED_BY_DESIGN.has(key))
      .filter((key) => typeof EN[key] === 'string' && typeof AR[key] === 'string')
      .filter((key) => EN[key] === AR[key]);

    expect(untranslated).toEqual([]);
  });

  it('keep every placeholder the English sentence promised', () => {
    // '{name} is suspended' translated without its {name} renders a sentence about nobody.
    for (const key of Object.keys(EN) as (keyof typeof EN)[]) {
      expect(
        [...placeholders(AR[key])].sort(),
        `placeholders differ for "${key}"`,
      ).toEqual([...placeholders(EN[key])].sort());
    }
  });

  it('give Arabic plurals all six forms', () => {
    // Arabic distinguishes zero, one, two, few, many and other. A translation that supplies only
    // `one` and `other`, mirroring English, is grammatical only by accident: three bookings and
    // eleven bookings take different forms of the noun, and both differ from two.
    const required: Intl.LDMLPluralRule[] = ['zero', 'one', 'two', 'few', 'many', 'other'];

    for (const key of Object.keys(EN) as (keyof typeof EN)[]) {
      const english = EN[key];
      if (typeof english === 'string') continue;

      const arabic = AR[key];
      expect(typeof arabic, `"${key}" is plural in English but not in Arabic`).not.toBe('string');
      // `zero` is only needed where English bothered to write one; the rest are always required.
      const forms = english as Partial<Record<Intl.LDMLPluralRule, string>>;
      const needed = required.filter((form) => form !== 'zero' || forms.zero !== undefined);
      const missing = needed.filter((form) => (arabic as Record<string, string>)[form] === undefined);
      expect(missing, `"${key}" is missing Arabic plural forms`).toEqual([]);
    }
  });
});
