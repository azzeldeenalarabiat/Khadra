import { PLATFORM_ID, inject } from '@angular/core';
import { isPlatformServer } from '@angular/common';
import { CanActivateFn } from '@angular/router';
import { AppConfigService } from '../config/app-config.service';
import { I18nService } from '../i18n/i18n.service';
import { isLanguage } from '../i18n/language';
import { injectResponseStatus } from '../http/server-context';

/**
 * Runs before any page under `/ar` or `/en`: fixes the page's language from its URL, and asks for the
 * platform configuration every page's dates and prices are printed with.
 *
 * On the server the render WAITS for the configuration, so the HTML a crawler reads has real dates in
 * the platform's zone rather than dashes; if the API cannot be reached the page still renders, with a
 * 503 so it is retried rather than indexed. In the browser nothing waits: the configuration usually
 * arrives with the page, and when it does not, the service keeps retrying while pages show loading.
 */
export const languageGuard: CanActivateFn = async (route) => {
  const language = route.data['language'];
  if (!isLanguage(language)) return false;
  inject(I18nService).use(language);

  const config = inject(AppConfigService);
  if (isPlatformServer(inject(PLATFORM_ID))) {
    const setStatus = injectResponseStatus();
    if ((await config.load()) === null) setStatus(503);
  } else {
    void config.load();
  }
  return true;
};
