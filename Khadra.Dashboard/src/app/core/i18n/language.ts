/**
 * The two languages the console speaks, and the shape a translated message can take.
 *
 * Kept in its own file so the dictionaries, the service and the switcher can share the types
 * without importing each other in a circle.
 */
export type Language = 'en' | 'ar';

export const LANGUAGES: readonly Language[] = ['en', 'ar'];

/**
 * A message is either one string, or one string per plural category.
 *
 * Arabic distinguishes SIX — zero, one, two, few, many, other — where English has two, and the
 * difference is not decorative: "٣ حجوزات" and "١١ حجزًا" take different forms of the same noun.
 * `Intl.PluralRules` picks the category; the dictionary supplies the words. English dictionaries
 * fill only `one` and `other`, and the lookup falls back to `other` for any category a language
 * does not use.
 */
export type Message = string | Readonly<Partial<Record<Intl.LDMLPluralRule, string>>>;

/** Values substituted into `{placeholders}`. */
export type MessageParams = Readonly<Record<string, string | number>>;
