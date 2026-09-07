import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { DealerConsoleService } from '../services/dealer-console.service';
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
  if (user.role === 'DealerEmployee') return '/employee/dashboard';
  return user.role === 'DealerOwner' ? '/dealer/dashboard' : '/dashboard';
}

/**
 * Where "your account" goes.
 *
 * The rail and the topbar are shared by both sides of the console, so a single `/security` link
 * would send dealer staff at an admin-only route and bounce them straight back to their dashboard —
 * a dead click that looks exactly like a broken one. Same reasoning as `homeRouteFor`, and answered
 * in the same place so the two cannot drift apart.
 */
export function accountRouteFor(user: SessionUser | null): string {
  if (!user) return '/sign-in';
  if (user.role === 'DealerEmployee') return '/employee/settings';
  return user.role === 'DealerOwner' ? '/dealer/settings' : '/security';
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

  // Employees have `/employee/*` now, so an old bookmark into `/dealer/*` lands them on their own
  // console rather than on the owner's with most of it closed. The dealer console's own permission
  // handling stays regardless: it is what an owner of a not-yet-trading dealership meets.
  return user.role === 'DealerOwner' ? true : router.createUrlTree([homeRouteFor(user)]);
};

/**
 * The employee's own console.
 *
 * An owner sent here is bounced to theirs rather than shown a stripped console that is missing the
 * screens they actually need — the two are different products now, not one with a filter on it.
 */
export const dealerEmployeeGuard: CanActivateFn = async () => {
  const session = inject(SessionService);
  const router = inject(Router);

  const user = await session.load();
  if (!user) return router.createUrlTree(['/sign-in']);

  return user.role === 'DealerEmployee' ? true : router.createUrlTree([homeRouteFor(user)]);
};

/** The mirror image: platform screens are for administrators. */
export const adminOnlyGuard: CanActivateFn = async () => {
  const session = inject(SessionService);
  const router = inject(Router);

  const user = await session.load();
  if (!user) return router.createUrlTree(['/sign-in']);

  return user.role === 'Admin' ? true : router.createUrlTree([homeRouteFor(user)]);
};

/**
 * A FORM only the owner may submit.
 *
 * Kept for the two vehicle forms and nothing else, because a form is the one place where hiding the
 * button is not enough: an employee who reaches `/dealer/fleet/new` by typing it can fill in five
 * steps and lose the lot to a bodiless 403 on save, which the screen can only render as "the service
 * did not respond". Read screens do not need this — they say in place that the action is the
 * owner's, which is more useful than a redirect that explains nothing.
 *
 * Costs no extra request: it waits on the same `GET /dealers/me` the gate is already loading. Where
 * that call failed, the route is allowed through so the gate stays the single place that explains a
 * dealership whose standing could not be read — the alternative is bouncing someone away from a page
 * with no reason given.
 *
 * Ownership only. A dealership that cannot trade is the gate's business, and it says so far better
 * than a redirect would.
 */
export const dealerOwnerGuard: CanActivateFn = async (_route, state) => {
  const console = inject(DealerConsoleService);
  const router = inject(Router);

  const dealer = await console.standing();
  if (!dealer || dealer.isOwner) return true;

  // The parent of the form, which carries the banner saying the fleet is the owner's to change.
  return router.createUrlTree([parentOf(state.url)]);
};

/** '/dealer/fleet/new' and '/dealer/fleet/:id/edit' both belong under the screen one level up. */
function parentOf(url: string): string {
  const path = url.split('?')[0].split('#')[0];
  const parent = path.slice(0, path.lastIndexOf('/'));
  return parent || '/dealer/fleet';
}
