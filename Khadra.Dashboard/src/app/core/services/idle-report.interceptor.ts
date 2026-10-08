import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { LiveRefreshService } from './live-refresh.service';

/** The BFF's name for it (`SessionActivity.IdleHeaderName`). The BFF strips it; the API never sees it. */
export const IDLE_REPORT_HEADER = 'X-Khadra-Idle-Seconds';

/**
 * Tells the BFF how long ago the person last used this page, on every call it makes (pre-launch item 129).
 *
 * The BFF ends a session thirty minutes after its PERSON was last active, on its own clock. A request
 * cannot say that by being sent — a background poll is sent by nobody — so each one says how long the
 * page has been untouched, and the BFF counts the request as activity at that moment. A click is a
 * report of zero; a poll from a console left open overnight reports the whole night, and moves nothing.
 *
 * Only on the console's own calls (`/api`, `/bff`): nothing else needs to know.
 */
export const idleReportInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.url.startsWith('/api/') && !request.url.startsWith('/bff/')) return next(request);
  const idle = inject(LiveRefreshService).idleSeconds();
  return next(request.clone({ setHeaders: { [IDLE_REPORT_HEADER]: String(idle) } }));
};
