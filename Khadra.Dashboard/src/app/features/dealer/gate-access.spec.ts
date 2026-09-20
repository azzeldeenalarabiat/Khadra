import { describe, expect, it } from 'vitest';
import { DealerStanding, OPEN_WHILE_LOCKED, OPEN_WHILE_SUSPENDED, lockedOut } from './gate-access';

/**
 * The dealer gate: which screens a dealership that cannot trade may still reach.
 *
 * The lists are pinned as written-out paths, not read back from the module, so a path dropped from
 * the module fails here instead of agreeing with itself — which is how `/dealer/customer-page` went
 * missing unnoticed.
 */
const TRADING: DealerStanding = { canTrade: true, isSuspended: false };
/** Under review, sent back for clarification, or rejected: not trading, not suspended. */
const LOCKED: DealerStanding = { canTrade: false, isSuspended: false };
const SUSPENDED: DealerStanding = { canTrade: false, isSuspended: true };

describe('dealer gate', () => {
  it('never covers a dealership that can trade', () => {
    for (const url of ['/dealer/dashboard', '/dealer/fleet', '/dealer/reports', '/employee/dashboard'])
      expect(lockedOut(url, TRADING)).toBe(false);
  });

  it('does not call an unknown standing a lock — the gate has waiting and unreachable states for that', () => {
    expect(lockedOut('/dealer/fleet', null)).toBe(false);
  });

  it('leaves an applicant the customer page, which the API serves them', () => {
    // `PUT me/public-profile` is `DealerOwner`, the same policy as `PUT me/profile`: the console
    // locked a screen whose save the API would have accepted.
    expect(lockedOut('/dealer/customer-page', LOCKED)).toBe(false);
  });

  it('leaves an applicant the dealer page and their account', () => {
    expect(lockedOut('/dealer/profile', LOCKED)).toBe(false);
    expect(lockedOut('/dealer/settings', LOCKED)).toBe(false);
    expect(lockedOut('/employee/settings', LOCKED)).toBe(false);
  });

  it('covers every screen that needs a trading dealership', () => {
    for (const url of [
      '/dealer/dashboard',
      '/dealer/bookings',
      '/dealer/fleet',
      '/dealer/reports',
      '/dealer/delivery',
      '/dealer/employees',
      '/employee/dashboard',
      '/employee/bookings',
    ])
      expect(lockedOut(url, LOCKED), url).toBe(true);
  });

  it('lets a suspended dealership record returns, and nothing it could trade with', () => {
    expect(lockedOut('/dealer/bookings', SUSPENDED)).toBe(false);
    // There is no bare disputes screen; the gate's entry is a prefix of `disputes/:ticketId`.
    expect(lockedOut('/dealer/disputes/01a08222-0228-7f9f-b051-fb28aca2ac4c', SUSPENDED)).toBe(false);
    expect(lockedOut('/employee/bookings', SUSPENDED)).toBe(false);
    expect(lockedOut('/dealer/customer-page', SUSPENDED)).toBe(false);
    expect(lockedOut('/dealer/fleet', SUSPENDED)).toBe(true);
    expect(lockedOut('/dealer/reports', SUSPENDED)).toBe(true);
  });

  it('opens exactly these paths', () => {
    expect([...OPEN_WHILE_LOCKED]).toEqual([
      '/dealer/profile',
      '/dealer/customer-page',
      '/dealer/settings',
      '/employee/settings',
    ]);
    expect([...OPEN_WHILE_SUSPENDED]).toEqual([
      '/dealer/bookings',
      '/dealer/disputes',
      '/employee/bookings',
      '/employee/disputes',
    ]);
  });
});
