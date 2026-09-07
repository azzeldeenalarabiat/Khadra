import { DOCUMENT } from '@angular/common';
import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { EN, TranslationKey } from './en';
import { AR } from './ar';
import { Language, LANGUAGES, Message, MessageParams } from './language';
import { resolveMessage } from './resolve';

const STORAGE_KEY = 'khadra.language';

const DICTIONARIES: Readonly<Record<Language, Readonly<Record<TranslationKey, Message>>>> = {
  en: EN,
  ar: AR,
};

/**
 * The console's language, and the one function that turns a key into words.
 *
 * ## Why this is a signal-reading function and not a pipe
 *
 * Most of this console's copy is not written in templates. It is built in `computed()`s and in pure
 * presenter functions — `dashboard.presenter.ts`, `booking-decisions.ts`, `nav.data.ts`, the label
 * rows in every detail screen. A translation *pipe* cannot reach any of that.
 *
 * Worse, the app is zoneless: there is no `zone.js`, so nothing re-renders unless a signal that the
 * view read has changed. A plain non-reactive lookup called inside a `computed` would not be
 * tracked, and after a switch those computeds would keep serving the old language until some
 * unrelated dependency happened to change — Arabic chrome around an English status pill. A *pure*
 * pipe is worse still: pure pipes memoise on their arguments, so the key is unchanged, and it hands
 * back the cached English however many times the view re-renders.
 *
 * So `t` reads `dictionary()` on every call. Any template expression or `computed()` that calls it
 * becomes a dependency of that signal, and every one of them recomputes on a switch. That is the
 * whole mechanism, and it is why `t` is an arrow property: components hold it as
 * `protected readonly t = inject(I18nService).t` and presenters take it as a parameter, so it can
 * be passed around without losing `this`.
 */
@Injectable({ providedIn: 'root' })
export class I18nService {
  private readonly doc = inject(DOCUMENT);

  private readonly language = signal<Language>('en');

  /**
   * Both dictionaries are imported statically rather than lazily.
   *
   * Two languages of UI copy is a few tens of kilobytes before compression, and `AR` is typed
   * against `keyof typeof EN`, so they are in the same compilation unit whatever the loading
   * strategy. Static import buys the thing that matters more here: `setLanguage` is synchronous, so
   * there is no window in which the direction has flipped but the words have not.
   */
  private readonly dictionary = computed(() => DICTIONARIES[this.language()]);

  readonly lang = this.language.asReadonly();
  readonly dir = computed<'ltr' | 'rtl'>(() => (this.language() === 'ar' ? 'rtl' : 'ltr'));
  readonly isRtl = computed(() => this.dir() === 'rtl');

  constructor() {
    // `lang` and `dir` belong on <html>, not on a wrapper: the scrollbar side, the default text
    // direction of fixed-position overlays, and `:lang()` selectors in the stylesheets all key off
    // the root element.
    effect(() => {
      const root = this.doc.documentElement;
      root.setAttribute('lang', this.language());
      root.setAttribute('dir', this.dir());
    });
  }

  readonly t = (key: TranslationKey, params?: MessageParams): string => {
    const dictionary = this.dictionary();
    const resolved = resolveMessage(dictionary[key] ?? EN[key], params, this.localeTag(), this.isRtl());
    // Never blank. A missing key shows itself, so it is found in a walkthrough rather than leaving
    // a hole nobody can describe.
    return resolved ?? key;
  };

  /** BCP 47 tag for `Intl`. Western digits are pinned; see FormatService. */
  localeTag(): string {
    return this.language() === 'ar' ? 'ar-JO-u-nu-latn' : 'en-GB';
  }

  setLanguage(language: Language): void {
    this.language.set(language);
    try {
      this.doc.defaultView?.localStorage.setItem(STORAGE_KEY, language);
    } catch {
      // A browser with site data blocked still gets to switch; it just will not be remembered.
    }
  }

  toggle(): void {
    this.setLanguage(this.language() === 'ar' ? 'en' : 'ar');
  }

  /**
   * The language to open in: what this person last chose, else what their browser asks for, else
   * English. Run before the first paint so nothing renders twice.
   */
  restore(): void {
    this.setLanguage(this.stored() ?? this.preferred() ?? 'en');
  }

  private stored(): Language | null {
    try {
      const value = this.doc.defaultView?.localStorage.getItem(STORAGE_KEY);
      return LANGUAGES.includes(value as Language) ? (value as Language) : null;
    } catch {
      return null;
    }
  }

  private preferred(): Language | null {
    const requested = this.doc.defaultView?.navigator.languages ?? [];
    for (const tag of requested) {
      const base = tag.toLowerCase().split('-')[0];
      if (base === 'ar' || base === 'en') return base;
    }
    return null;
  }
}
