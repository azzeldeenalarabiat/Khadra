import { LocalizedText } from '../models/localized.api';
import { Language } from './language';

/**
 * Editing dealer-authored text in both languages: the order the boxes are drawn, and what the API
 * calls each one.
 *
 * Its own file because two screens write this content — the customer page and the registration form
 * — and a second copy of either rule in either screen is a way for them to disagree about a field
 * name the server validates.
 *
 * The language type is the console's own `Language`, not a second `'ar' | 'en'`: an office writes in
 * the same two languages the console speaks, and two spellings of that fact would be one too many.
 */

/**
 * Arabic first, deliberately, and NOT `LANGUAGES` from `core/i18n/language`, which is English first.
 *
 * That one is the order of the console's own language switcher. This is the order an office writes
 * its page in: the customers are in Jordan. The two being different is the reason this has its own
 * name — imported as `LANGUAGES`, one would silently become the other.
 */
export const CONTENT_LANGUAGES: readonly Language[] = ['ar', 'en'];

/**
 * The key one box is held under in a draft, and the multipart field name it is submitted as:
 * `rentalConditionsAr`, `rentalConditionsEn`, `descriptionAr`.
 *
 * The same string the SERVER names in a validation error, so a per-language message lands under the
 * box that caused it without a screen mapping anything.
 *
 * The suffix is the language tag itself, Pascal-cased, because that IS the API's property name —
 * `RentalConditionsAr` over the wire as `rentalConditionsAr`. Derived rather than listed, so there is
 * no table of suffixes here to fall out of step with the contract.
 */
export function boxKey(field: string, language: Language): string {
  return field + language.charAt(0).toUpperCase() + language.slice(1);
}

/**
 * Every name a refusal about ONE box can arrive under, in the order a screen should try them.
 *
 * Three layers name it, and the console chooses none of them:
 *
 * - the command validator, through FluentValidation's child rules, as a PATH: `about.Ar`, and
 *   `details.Description.Ar` for a car, because the car's fields travel inside `Details`;
 * - the domain, through `PublicProfileSection.FieldNameFor`, flat: `aboutAr`;
 * - the multipart binder on the application form, from the form field itself: `DescriptionAr`.
 *
 * `fieldMessageFor` matches a name against the tail of each key's path, so the dotted form here
 * answers for the wrapped one too, and the flat form covers the other two. Both spellings are needed:
 * one of them alone leaves the message off the box most of the time, which is how a per-language
 * refusal ends up as a banner about "a form" and an owner is left to guess which of two boxes to fix.
 *
 * `Khadra.Tests/Application/Common/BilingualFieldNameTests.cs` pins the server's side of this.
 */
export function boxErrorNames(field: string, language: Language): readonly string[] {
  return [`${field}.${language}`, boxKey(field, language)];
}

/** One written language of a pair, ready to render: the words and the language they are in. */
export interface WrittenText {
  readonly language: Language;
  readonly text: string;
}

/**
 * The languages a pair is actually written in, in draw order, for a screen that shows RAW values.
 *
 * For an Admin or a member of staff READING what an office wrote, not for a customer. No fallback is
 * applied and none should be: an administrator reviewing an application needs to see that the office
 * filled in one language and not the other, and a screen that quietly showed the English under an
 * Arabic heading would hide exactly the thing being reviewed. A customer's fallback is the server's,
 * resolved once, and arrives as a `ResolvedText` instead.
 *
 * Empty when nothing is written, so the screen can say so rather than printing a blank row.
 */
export function writtenIn(text: LocalizedText | null | undefined): readonly WrittenText[] {
  if (!text) return [];
  return CONTENT_LANGUAGES.map((language) => ({ language, text: (text[language] ?? '').trim() }))
    .filter((written) => written.text !== '');
}
