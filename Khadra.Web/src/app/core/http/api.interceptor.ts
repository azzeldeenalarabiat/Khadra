import { HttpErrorResponse, HttpInterceptorFn, HttpResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, catchError, from, of, switchMap, tap, throwError, timeout } from 'rxjs';
import { I18nService } from '../i18n/i18n.service';
import { SessionService } from '../session/session.service';
import { readServerCache, serverCacheKey, writeServerCache } from './server-cache';
import { injectServerContext } from './server-context';
import { XsrfService } from './xsrf.service';

/** How long a server render waits for the API before rendering an honest "not answering" state. */
const SERVER_TIMEOUT_MS = 20_000;

const WRITES = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);

/**
 * Every call to the API, in both places a page is built.
 *
 * In the BROWSER a call goes same-origin to the customer BFF, which holds the session and attaches
 * the API token itself; the page never sees one. Writes carry the antiforgery token the BFF demands.
 *
 * On the SERVER a call goes straight to the API, anonymously: the renderer builds public pages only,
 * never receives the visitor's cookie, and passes on the visitor's address only when the BFF vouched
 * for it. Account data is never fetched there — those pages render in the browser.
 *
 * In both, `Accept-Language` is the language of the URL being rendered — never the browser's header —
 * so a cached page can only ever be in the language its address says.
 */
export const apiInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.url.startsWith('/api/') && !request.url.startsWith('/bff/')) return next(request);

  const language = inject(I18nService).language();
  const server = injectServerContext();

  if (server) {
    if (request.url.startsWith('/bff/')) {
      throw new Error(`The renderer has no session, so it cannot call ${request.url}.`);
    }

    const cacheKey = request.method === 'GET' ? serverCacheKey(request.url, language) : null;
    const cached = cacheKey ? readServerCache(cacheKey) : undefined;
    if (cached !== undefined) return of(new HttpResponse({ status: 200, body: cached, url: request.url }));

    let headers = request.headers.set('Accept-Language', language).delete('Cookie');
    if (server.clientAddress) headers = headers.set('X-Forwarded-For', server.clientAddress);

    return next(
      request.clone({ url: server.apiBaseUrl.replace(/\/$/, '') + request.url, headers, withCredentials: false }),
    ).pipe(
      timeout(SERVER_TIMEOUT_MS),
      tap((event) => {
        if (cacheKey && event instanceof HttpResponse && event.status === 200) writeServerCache(cacheKey, event.body);
      }),
    );
  }

  const session = inject(SessionService);
  const router = inject(Router);
  const i18n = inject(I18nService);

  // The BFF answers 401 once the API has disowned a session (password changed elsewhere, account
  // suspended, refresh expired) and has already signed it out. Say so, and send the customer to sign
  // in and straight back — never leave a page of silent failures behind a header that says "signed in".
  const ended = <T>(source: Observable<T>) =>
    source.pipe(
      catchError((error: unknown) => {
        if (
          error instanceof HttpErrorResponse &&
          error.status === 401 &&
          !request.url.startsWith('/bff/') &&
          session.isSignedIn()
        ) {
          session.markEnded();
          void router.navigate(i18n.link('login'), {
            queryParams: { returnUrl: router.url, reason: 'ended' },
          });
        }
        return throwError(() => error);
      }),
    );

  const withLanguage = request.clone({ setHeaders: { 'Accept-Language': language } });
  if (!WRITES.has(request.method)) return ended(next(withLanguage));

  const xsrf = inject(XsrfService);
  return ended(
    from(xsrf.token()).pipe(
      switchMap((token) => next(withLanguage.clone({ setHeaders: { 'X-XSRF-TOKEN': token } }))),
    ),
  );
};
