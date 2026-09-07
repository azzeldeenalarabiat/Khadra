import {
  provideHttpClient,
  withFetch,
  withInterceptors,
  withXsrfConfiguration,
} from '@angular/common/http';
import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { TitleStrategy, provideRouter } from '@angular/router';
import { routes } from './app.routes';
import { I18nService } from './core/i18n/i18n.service';
import { TranslatedTitleStrategy } from './core/i18n/translated-title.strategy';
import { sessionExpiredInterceptor } from './core/services/session-expired.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // Before the first paint, so the console opens in the language this person last chose rather
    // than rendering in English and flipping. It also puts `lang` and `dir` on <html> in time for
    // the first layout, which is what stops an Arabic session drawing left-to-right for a frame.
    provideAppInitializer(() => inject(I18nService).restore()),
    provideRouter(routes),
    // Angular resolves a route's static `title` once, on activation, so a language switch never
    // reaches the browser tab without this.
    { provide: TitleStrategy, useClass: TranslatedTitleStrategy },
    // Calls go to the BFF, which authenticates them with the session cookie and proxies to the API.
    // The cookie is HttpOnly, so it is the antiforgery pair below that the console has to participate
    // in; the names match what Khadra.Bff issues.
    provideHttpClient(
      withFetch(),
      withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'X-XSRF-TOKEN' }),
      withInterceptors([sessionExpiredInterceptor]),
    ),
  ],
};
