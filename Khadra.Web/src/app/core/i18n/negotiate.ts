import { DEFAULT_LANGUAGE, Language } from './language';

/**
 * Which language `/` sends a visitor to, from their browser's `Accept-Language`.
 *
 * The rule the owner set (2026-09-23): English only when the browser CLEARLY prefers English, and
 * Arabic otherwise. "Clearly" means English is the most preferred language the header names and no
 * Arabic tag is preferred as much. A header that is missing, malformed, or leads with anything else
 * (French, say) is not clear, and gets Arabic.
 *
 * Used for that one redirect only. Every other page takes its language from its URL.
 */
export function negotiateLanguage(acceptLanguage: string | null | undefined): Language {
  if (!acceptLanguage) return DEFAULT_LANGUAGE;

  const ranges = acceptLanguage
    .split(',')
    .map((part, index) => {
      const [tag, ...params] = part.trim().split(';');
      const qParam = params.map((p) => p.trim()).find((p) => p.startsWith('q='));
      const q = qParam === undefined ? 1 : Number(qParam.slice(2));
      return { tag: (tag ?? '').trim().toLowerCase(), q: Number.isFinite(q) ? q : 0, index };
    })
    .filter((range) => range.tag !== '' && range.tag !== '*' && range.q > 0)
    // Highest weight first; among equals, the order the browser wrote them in.
    .sort((a, b) => b.q - a.q || a.index - b.index);

  const top = ranges[0];
  if (!top || !isEnglish(top.tag)) return DEFAULT_LANGUAGE;

  const arabicAsPreferred = ranges.some((range) => isArabic(range.tag) && range.q >= top.q);
  return arabicAsPreferred ? DEFAULT_LANGUAGE : 'en';
}

function isEnglish(tag: string): boolean {
  return tag === 'en' || tag.startsWith('en-');
}

function isArabic(tag: string): boolean {
  return tag === 'ar' || tag.startsWith('ar-');
}
