// Loaded before anything Angular: the guards module imports DI tokens with partially compiled decorators.
import '@angular/compiler';
import { describe, expect, it } from 'vitest';
import { EMPLOYEE_NAV, SCREEN_TITLES } from '../data/nav.data';
import { SessionUser } from '../services/session.service';
import { dealerConsoleRoot, homeRouteFor } from './role.guards';

const person = (role: SessionUser['role']) => ({ role }) as SessionUser;

/**
 * Reports and Payouts for an employee the owner granted them (E2E F19). The grant existed and the API
 * honoured it, but the employee console had no screen to use it on, and the owner's screens linked into
 * `/dealer/...` — the owner's console, which sends an employee straight back to their own dashboard.
 */
describe('dealerConsoleRoot', () => {
  it('keeps an employee inside the employee console and the owner inside theirs', () => {
    expect(dealerConsoleRoot(person('DealerEmployee'))).toBe('/employee');
    expect(dealerConsoleRoot(person('DealerOwner'))).toBe('/dealer');
  });

  it('agrees with where each of them is sent home', () => {
    for (const role of ['DealerEmployee', 'DealerOwner'] as const) {
      expect(homeRouteFor(person(role)).startsWith(dealerConsoleRoot(person(role)))).toBe(true);
    }
  });

  it('answers the owner console before the session has loaded, as the owner rail does', () => {
    expect(dealerConsoleRoot(null)).toBe('/dealer');
    expect(dealerConsoleRoot(undefined)).toBe('/dealer');
  });
});

describe('the employee rail', () => {
  const finance = EMPLOYEE_NAV.find((group) => group.groupKey === 'nav.group.finance');

  it('offers Reports and Payouts only to an employee holding the reports grant', () => {
    expect(finance?.items.map((item) => [item.route, item.requires])).toEqual([
      ['/employee/reports', 'view-reports'],
      ['/employee/payouts', 'view-reports'],
    ]);
  });

  it('never points an employee into the owner console', () => {
    const routes = EMPLOYEE_NAV.flatMap((group) => group.items.map((item) => item.route));
    expect(routes.every((route) => route.startsWith('/employee/'))).toBe(true);
  });

  it('titles both new screens', () => {
    expect(SCREEN_TITLES['employee/reports']).toBe('nav.reports');
    expect(SCREEN_TITLES['employee/payouts']).toBe('nav.payouts');
  });
});
