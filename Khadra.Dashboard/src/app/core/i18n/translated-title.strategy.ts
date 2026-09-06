import { Injectable, effect, inject, untracked } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';
import { SCREEN_TITLES } from '../data/nav.data';
import { TranslationKey } from './en';
import { I18nService } from './i18n.service';

/**
 * The browser tab's title, in the language the console is speaking.
 *
 * Angular resolves a route's static `title` once, when the route activates, so a language switch
 * never reaches it: the console would flip to Arabic and leave "Booking details · Khadra Admin" in
 * the tab, the window list and any bookmark made from it.
 *
 * The screen names are already keyed in `SCREEN_TITLES`, which the breadcrumb and the page heading
 * both read, so the tab reuses those rather than keeping a parallel list that can drift. The routes
 * that sit outside the shell — the auth cards, which have no `SCREEN_TITLES` entry because they have
 * no breadcrumb — are named here.
 *
 * A route this does not recognise keeps whatever static title it declared, so an unmapped screen
 * shows English rather than nothing.
 */
const AUTH_TITLES: Readonly<Record<string, TranslationKey>> = {
  'sign-in': 'auth.signIn.title',
  register: 'auth.register.title',
  'forgot-password': 'auth.forgot.title',
  'reset-password': 'auth.reset.title',
  'verify-email': 'auth.verify.title',
  'accept-invitation': 'auth.invite.title',
  'dealer/apply': 'screen.submitGallery',
};

@Injectable()
export class TranslatedTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);
  private readonly i18n = inject(I18nService);

  /** The last route to activate, so a language switch can re-title it without a navigation. */
  private current: RouterStateSnapshot | null = null;

  constructor() {
    super();
    // Re-title on every switch. `untracked` around the write keeps this an effect that depends on
    // the language alone, rather than on whatever the title setter happens to read.
    effect(() => {
      this.i18n.lang();
      untracked(() => {
        if (this.current) this.updateTitle(this.current);
      });
    });
  }

  override updateTitle(snapshot: RouterStateSnapshot): void {
    this.current = snapshot;

    const key = this.keyFor(snapshot.url);
    if (key) {
      this.title.setTitle(`${this.i18n.t(key)} · ${this.i18n.t('app.name')}`);
      return;
    }

    const declared = this.buildTitle(snapshot);
    if (declared) this.title.setTitle(declared);
  }

  /** '/dealers/019a…?x=1' becomes 'dealers/:id', the shape `SCREEN_TITLES` is keyed by. */
  private keyFor(url: string): TranslationKey | undefined {
    const path = url.split('?')[0].split('#')[0].replace(/^\/+|\/+$/g, '');
    if (!path) return undefined;

    const normalised = path
      .split('/')
      .map((segment) => (/^[0-9a-f]{8}-|^[0-9a-f]{26,}$/i.test(segment) ? ':id' : segment))
      .join('/');

    return AUTH_TITLES[normalised] ?? SCREEN_TITLES[normalised];
  }
}
