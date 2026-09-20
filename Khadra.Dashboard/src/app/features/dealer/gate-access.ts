/**
 * Which dealer screens stay reachable while a dealership cannot trade.
 *
 * Split out of `DealerGateComponent` so the rule has a spec: the console has no per-component specs,
 * and a list of paths is exactly the kind of thing that drifts from the API it is meant to mirror.
 * It already had: `/dealer/customer-page` shipped without an entry here, so an applicant was locked
 * out of a screen whose save endpoint serves them.
 */

/**
 * Reachable while locked: what an applicant is preparing, and their own account.
 *
 * A screen belongs here when the API serves its writes to an owner whose dealership cannot trade yet
 * — so this follows the authorization, not a view of what an applicant ought to see.
 * `PUT me/profile` and `PUT me/public-profile` are both `DealerOwner`, not the approved-dealer
 * policy, so both pages are open. The employee console has only its account here: an employee has no
 * application to prepare.
 */
export const OPEN_WHILE_LOCKED: readonly string[] = [
  '/dealer/profile',
  '/dealer/customer-page',
  '/dealer/settings',
  '/employee/settings',
];

/**
 * A SUSPENDED dealer still has customers holding its cars. Returns must be recordable, so the
 * bookings screens stay open; approving and rejecting are hidden there by the booking screen
 * itself, and the API refuses them regardless.
 */
export const OPEN_WHILE_SUSPENDED: readonly string[] = [
  '/dealer/bookings',
  '/dealer/disputes',
  '/employee/bookings',
  '/employee/disputes',
];

/** The two facts the gate decides on, as `GET /dealers/me` reports them. */
export interface DealerStanding {
  readonly canTrade: boolean;
  readonly isSuspended: boolean;
}

/**
 * Whether the gate's card covers the screen at `url`.
 *
 * Only a dealership that cannot trade is ever locked out, and never off the screens that stay its
 * own. A standing that is not known yet is not "locked" either — the gate has its own waiting and
 * unreachable states for that, so an unanswered `GET /dealers/me` is shown as exactly that rather
 * than as a lock the dealer could do nothing about.
 */
export function lockedOut(url: string, dealer: DealerStanding | null): boolean {
  if (!dealer || dealer.canTrade) return false;
  const open = dealer.isSuspended
    ? [...OPEN_WHILE_LOCKED, ...OPEN_WHILE_SUSPENDED]
    : OPEN_WHILE_LOCKED;
  return !open.some((path) => url.startsWith(path));
}
