import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { SessionService } from '../services/session.service';

/**
 * Keeps the console behind a session.
 *
 * This is a convenience, not a security boundary: the BFF refuses every `/api` call without a valid
 * cookie regardless of what the browser router allows. What it buys is that an unauthenticated
 * visitor lands on the sign-in screen instead of a fully drawn dashboard full of error states.
 */
export const adminSessionGuard: CanActivateFn = async (_route, state) => {
  const session = inject(SessionService);
  const router = inject(Router);

  const user = await session.load();
  if (user) return true;

  // Carries where they were going, so signing in resumes it rather than always landing on /dashboard.
  return router.createUrlTree(['/sign-in'], { queryParams: { returnUrl: state.url } });
};
