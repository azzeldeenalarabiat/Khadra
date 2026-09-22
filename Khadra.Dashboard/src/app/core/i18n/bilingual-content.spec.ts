import { describe, expect, it } from 'vitest';
import { CONTENT_LANGUAGES, boxErrorNames, boxKey } from './bilingual-content';
import { LANGUAGES } from './language';

/**
 * The names one box of dealer-authored text is known by.
 *
 * Small, and worth pinning precisely, because everything downstream is a string comparison: the
 * multipart field the application form submits, the key a per-language refusal is looked up under,
 * and the id a label points at. A quiet change to any of them shows up as a message that simply
 * does not appear, under a box that looks fine.
 *
 * The server's half is pinned in `Khadra.Tests/Application/Common/BilingualFieldNameTests.cs`.
 */
describe('the two languages dealer content is written in', () => {
  it('draws Arabic first, and is NOT the console’s own language list', () => {
    // Two constants, two orders, on purpose: the switcher offers English first, an office writes
    // for Jordan. Imported as the same name, one would silently become the other.
    expect(CONTENT_LANGUAGES).toEqual(['ar', 'en']);
    expect(LANGUAGES).toEqual(['en', 'ar']);
  });

  it('covers both languages and nothing else', () => {
    expect([...CONTENT_LANGUAGES].sort()).toEqual([...LANGUAGES].sort());
  });
});

describe('what the server calls one box', () => {
  it('spells the flat name the way the API spells its own property', () => {
    // `RentalConditionsAr` on the command, `rentalConditionsAr` on the wire, and the same string as
    // a multipart field name on the application form.
    expect(boxKey('rentalConditions', 'ar')).toBe('rentalConditionsAr');
    expect(boxKey('rentalConditions', 'en')).toBe('rentalConditionsEn');
    expect(boxKey('description', 'ar')).toBe('descriptionAr');
    expect(boxKey('about', 'en')).toBe('aboutEn');
  });

  it('asks for the dotted path first and the flat name second', () => {
    // The dotted one is FluentValidation's, and `fieldMessageFor` matches it against the tail of a
    // key — so it answers for `details.Description.Ar` as well. The flat one is the domain's and the
    // multipart binder's. Both are needed; either alone misses most refusals.
    expect(boxErrorNames('description', 'ar')).toEqual(['description.ar', 'descriptionAr']);
    expect(boxErrorNames('about', 'en')).toEqual(['about.en', 'aboutEn']);
  });

  it('never returns the same name twice, for either language', () => {
    for (const language of CONTENT_LANGUAGES) {
      const names = boxErrorNames('insurance', language);
      expect(new Set(names).size).toBe(names.length);
    }
  });
});
