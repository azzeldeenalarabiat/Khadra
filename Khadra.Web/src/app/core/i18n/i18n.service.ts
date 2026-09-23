import { DOCUMENT, Injectable, computed, inject, signal } from '@angular/core';
import { AR } from './ar';
import { EN, TranslationKey } from './en';
import { DEFAULT_LANGUAGE, Language, MessageParams, localeTagFor } from './language';
import { resolveMessage } from './resolve';

/**
 * The language of the page being rendered, and every word in it.
 *
 * Safe on the server: it never reads `localStorage` or `navigator`. The language is set from the URL
 * by the route guard on `/ar` and `/en` before any page component runs, and `<html lang dir>` is
 * written through `DOCUMENT` so the server-rendered HTML already carries the right direction.
 */
@Injectable({ providedIn: 'root' })
export class I18nService {
  private readonly document = inject(DOCUMENT);

  readonly language = signal<Language>(DEFAULT_LANGUAGE);
  readonly localeTag = computed(() => localeTagFor(this.language()));
  readonly direction = computed(() => (this.language() === 'ar' ? 'rtl' : 'ltr'));
  readonly isArabic = computed(() => this.language() === 'ar');

  private readonly dictionary = computed(() => (this.language() === 'ar' ? AR : EN));

  use(language: Language): void {
    this.language.set(language);
    const root = this.document.documentElement;
    root.setAttribute('lang', language);
    root.setAttribute('dir', language === 'ar' ? 'rtl' : 'ltr');
  }

  t(key: TranslationKey, params?: MessageParams): string {
    const language = this.language();
    return (
      resolveMessage(this.dictionary()[key], params, this.localeTag(), language === 'ar') ??
      resolveMessage(EN[key], params, localeTagFor('en'), false) ??
      key
    );
  }

  /** A router link inside the current language: `link('cars', id)` → `['/', 'ar', 'cars', id]`. */
  link(...segments: readonly (string | number)[]): (string | number)[] {
    return ['/', this.language(), ...segments];
  }

  /** The same page in the other language: the path with its first segment swapped, query kept. */
  static switchedUrl(url: string, to: Language): string {
    const [pathAndQuery, fragment] = url.split('#', 2);
    const [path, query] = (pathAndQuery ?? '').split('?', 2);
    const segments = (path ?? '').split('/').filter((segment) => segment !== '');
    if (segments[0] === 'ar' || segments[0] === 'en') segments[0] = to;
    else segments.unshift(to);
    return (
      '/' +
      segments.join('/') +
      (query !== undefined ? `?${query}` : '') +
      (fragment !== undefined ? `#${fragment}` : '')
    );
  }
}
