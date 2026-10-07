import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { ConsentGateService } from '../services/consent-gate.service';
import { SessionService } from '../services/session.service';

/**
 * Keeps the console behind a session.
 *
 * This is a convenience, not a security boundary: the BFF refuses every `/api` call without a valid
 * cookie regardless of what the browser router allows. What it buys is that an unauthenticated
 * visitor lands on the sign-in screen instead of a fully drawn dashboard full of error states.
 *
 * It also waits to learn whether a legal text in force awaits this person's consent (Wave 4, W4-8), so a frame that
 * the consent prompt is about to cover is never drawn, and nothing it would ask is sent only to be refused.
 */
export const adminSessionGuard: CanActivateFn = async (_route, state) => {
  const session = inject(SessionService);
  const consent = inject(ConsentGateService);
  const router = inject(Router);

  const user = await session.load();
  if (user) {
    await consent.settle();
    return true;
  }

  // Carries where they were going, so signing in resumes it rather than always landing on /dashboard.
  return router.createUrlTree(['/sign-in'], { queryParams: { returnUrl: state.url } });
};
