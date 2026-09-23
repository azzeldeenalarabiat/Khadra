import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { I18nService } from '../i18n/i18n.service';
import { SessionService } from './session.service';

/**
 * Pages that belong to an account. The guard is a courtesy — every endpoint behind these pages is
 * refused by the API without a session, whatever the browser does — but it sends a visitor to sign in
 * and back, rather than to a page of refusals.
 */
export const signedInGuard: CanActivateFn = async (_route, state) => {
  const session = inject(SessionService);
  const router = inject(Router);
  const i18n = inject(I18nService);
  const resolved = await session.resolve();
  if (resolved.status === 'signed-in') return true;
  return router.createUrlTree(i18n.link('login'), { queryParams: { returnUrl: state.url } });
};

/** Sign-in and registration: somebody already signed in goes where they were heading instead. */
export const signedOutGuard: CanActivateFn = async (route) => {
  const session = inject(SessionService);
  const router = inject(Router);
  const i18n = inject(I18nService);
  const resolved = await session.resolve();
  if (resolved.status !== 'signed-in') return true;
  return router.parseUrl(safeReturnUrl(route.queryParamMap.get('returnUrl'), i18n.language()));
}

/**
 * Only a path on this site, in a language: never another origin (`//evil.example`), never a scheme.
 * A return address is data a stranger can put in a link, so it is checked before it is followed.
 */
export function safeReturnUrl(value: string | null | undefined, language: string): string {
  if (value && /^\/(ar|en)(\/|\?|$)/.test(value) && !value.startsWith('//') && !/[\\\s]/.test(value)) return value;
  return `/${language}`;
}
