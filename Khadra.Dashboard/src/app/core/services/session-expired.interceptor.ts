import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { SessionService } from './session.service';

/**
 * A 401 on a call that was made WITH a session means the session is over, not that the request was
 * wrong: the BFF signs the cookie out the moment the API disowns its token (password changed
 * elsewhere, account suspended, employee deactivated). Without this, every screen would fail one call
 * at a time while the frame still showed the person as signed in.
 *
 * Sign-in itself is excluded: there a 401 means "wrong password", and the sign-in screen says so.
 */
export const sessionExpiredInterceptor: HttpInterceptorFn = (request, next) => {
  const session = inject(SessionService);
  const router = inject(Router);

  return next(request).pipe(
    catchError((error: unknown) => {
      if (
        error instanceof HttpErrorResponse &&
        error.status === 401 &&
        !request.url.includes('/bff/login') &&
        !request.url.includes('/bff/user') &&
        session.user()
      ) {
        session.forget();
        void router.navigate(['/sign-in'], {
          queryParams: { returnUrl: router.url, reason: 'session-ended' },
        });
      }
      return throwError(() => error);
    }),
  );
};
