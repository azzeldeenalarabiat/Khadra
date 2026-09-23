/**
 * The two languages the website speaks, and the shape a translated message can take.
 *
 * The language is part of every public URL (`/ar/...`, `/en/...`): search engines index each language
 * as its own page, a shared link opens in the language it was shared in, and the server renders the
 * right words before any script runs. Nothing about language is read from the browser except the
 * one-time redirect at `/`, which the server does.
 */
export type Language = 'ar' | 'en';

export const LANGUAGES: readonly Language[] = ['ar', 'en'];

/** Arabic is the market's language and the fallback whenever nothing clearer is known (owner, 2026-09-23). */
export const DEFAULT_LANGUAGE: Language = 'ar';

export function isLanguage(value: unknown): value is Language {
  return value === 'ar' || value === 'en';
}

/**
 * Latin digits in both languages, as the app and the console print them: a price, a booking reference
 * and a plate number beside each other must not switch numeral systems mid-line.
 */
export function localeTagFor(language: Language): string {
  return language === 'ar' ? 'ar-JO-u-nu-latn' : 'en-GB';
}

/**
 * A message is either one string, or one string per plural category. Arabic uses all six categories.
 */
export type Message = string | Readonly<Partial<Record<Intl.LDMLPluralRule, string>>>;

/** Values substituted into `{placeholders}`. */
export type MessageParams = Readonly<Record<string, string | number>>;
