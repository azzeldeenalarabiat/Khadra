import { Message, MessageParams } from './language';

/** First directional isolate / pop directional isolate (U+2068 / U+2069). */
const FSI = '⁨';
const PDI = '⁩';

/**
 * Turns one dictionary entry into the sentence it stands for.
 *
 * Split out of `I18nService` so the service and the specs share one implementation: a presenter test
 * asserts on the words a reader sees, and it can only do that honestly if it resolves them the same
 * way the console does — same plural rules, same interpolation, same isolates.
 */
export function resolveMessage(
  message: Message | undefined,
  params: MessageParams | undefined,
  localeTag: string,
  isolateParams: boolean,
): string | undefined {
  if (message === undefined) return undefined;
  return interpolate(select(message, params, localeTag), params, isolateParams);
}

/**
 * The plural form for `params.count`, or the plain string when there is only one form.
 *
 * `other` is the fallback for a category the dictionary does not define, which is what lets an
 * English entry supply `one`/`other` and still answer when Arabic asks for `few`.
 */
function select(message: Message, params: MessageParams | undefined, localeTag: string): string {
  if (typeof message === 'string') return message;
  const count = params?.['count'];
  if (typeof count !== 'number') return message.other ?? '';
  const category = new Intl.PluralRules(localeTag).select(count);
  return message[category] ?? message.other ?? '';
}

function interpolate(
  text: string,
  params: MessageParams | undefined,
  isolateParams: boolean,
): string {
  if (!params) return text;
  return text.replace(/\{(\w+)\}/g, (whole, name: string) => {
    const value = params[name];
    if (value === undefined) return whole;
    // A Latin name, an email or a "+962…" number dropped into an Arabic sentence reorders the words
    // around it without this. The isolates are zero-width and only added for Arabic, so English
    // output is byte-for-byte what it was.
    return isolateParams ? `${FSI}${String(value)}${PDI}` : String(value);
  });
}
