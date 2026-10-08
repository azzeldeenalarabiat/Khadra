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
import { PlatformConfigService } from './core/services/platform-config.service';
import { consentGateInterceptor } from './core/services/consent-gate.interceptor';
import { idleReportInterceptor } from './core/services/idle-report.interceptor';
import { sessionExpiredInterceptor } from './core/services/session-expired.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // Before the first paint, so the console opens in the language this person last chose rather
    // than rendering in English and flipping. It also puts `lang` and `dir` on <html> in time for
    // the first layout, which is what stops an Arabic session drawing left-to-right for a frame.
    provideAppInitializer(() => inject(I18nService).restore()),
    // The currency's scale and the platform's reporting zone, read from the server rather than
    // assumed. Not awaited: a slow or unreachable call must not hold the console at a blank
    // screen, and the formatter falls back to the behaviour it had before it asked.
    provideAppInitializer(() => {
      void inject(PlatformConfigService).load();
    }),
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
      // A refusal for a pending consent raises the prompt (Wave 4, W4-8); see ConsentGateService.
      // Every call says how long its person has been idle, so the BFF's idle timeout cannot be held
      // open by background polling (pre-launch item 129); see idleReportInterceptor.
      withInterceptors([idleReportInterceptor, sessionExpiredInterceptor, consentGateInterceptor]),
    ),
  ],
};
