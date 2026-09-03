import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { SessionService, SessionUser } from '../services/session.service';

/**
 * Where a signed-in user belongs.
 *
 * Spec 1.5 puts Admin, Dealer Owner and Employee behind one dashboard with role-based views, so
 * "home" is not a fixed route. Keeping the answer in one function means the sign-in redirect, the
 * empty-path redirect and any future "back to start" link cannot disagree.
 */
export function homeRouteFor(user: SessionUser | null): string {
  if (!user) return '/sign-in';
  return user.role === 'DealerOwner' || user.role === 'DealerEmployee' ? '/fleet' : '/dashboard';
}

/**
 * Dealer staff only.
 *
 * A convenience, not a security boundary: the API refuses every dealer route to anyone else
 * regardless. What it buys is that an admin who wanders onto /fleet is sent somewhere useful instead
 * of being shown a screen full of 403s.
 */
export const dealerStaffGuard: CanActivateFn = async () => {
  const session = inject(SessionService);
  const router = inject(Router);

  const user = await session.load();
  if (!user) return router.createUrlTree(['/sign-in']);

  return user.role === 'DealerOwner' || user.role === 'DealerEmployee'
    ? true
    : router.createUrlTree([homeRouteFor(user)]);
};

/** The mirror image: platform screens are for administrators. */
export const adminOnlyGuard: CanActivateFn = async () => {
  const session = inject(SessionService);
  const router = inject(Router);

  const user = await session.load();
  if (!user) return router.createUrlTree(['/sign-in']);

  return user.role === 'Admin' ? true : router.createUrlTree([homeRouteFor(user)]);
};
