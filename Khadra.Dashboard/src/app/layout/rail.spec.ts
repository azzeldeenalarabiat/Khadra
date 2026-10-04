// Loaded before anything Angular: the services the types come from are DI tokens with partially compiled decorators.
import '@angular/compiler';
import { describe, expect, it } from 'vitest';
import { DEALER_NAV, EMPLOYEE_NAV } from '../core/data/nav.data';
import { DealerPermissions } from '../core/services/dealer-console.service';
import { railFor } from './rail';

const employee = (canViewReports: boolean): DealerPermissions => ({
  isOwner: false,
  canTrade: true,
  canEditProfile: false,
  canManageFleet: false,
  canManageStaff: false,
  canEditDelivery: false,
  canDecideBookings: true,
  canViewReports,
});

const routes = (groups: ReturnType<typeof railFor>) => groups.flatMap((group) => group.items.map((item) => item.route));

/** Reports and Payouts for an employee only when the owner granted them (E2E F19). */
describe('the employee rail', () => {
  it('shows a granted employee the Finance group: Reports and Payouts, inside the employee console', () => {
    const rail = railFor(EMPLOYEE_NAV, employee(true));

    expect(rail.map((group) => group.groupKey)).toContain('nav.group.finance');
    expect(routes(rail)).toEqual(expect.arrayContaining(['/employee/reports', '/employee/payouts']));
  });

  it('drops the whole Finance group for an employee without the grant, and keeps everything else', () => {
    const rail = railFor(EMPLOYEE_NAV, employee(false));

    expect(rail.map((group) => group.groupKey)).not.toContain('nav.group.finance');
    expect(routes(rail)).not.toContain('/employee/reports');
    expect(routes(rail)).toEqual(expect.arrayContaining(['/employee/dashboard', '/employee/bookings', '/employee/fleet']));
  });

  it('omits every gated item until the permissions have answered, rather than guessing', () => {
    const rail = railFor(EMPLOYEE_NAV, null);

    expect(routes(rail)).not.toContain('/employee/reports');
    expect(routes(rail)).not.toContain('/employee/payouts');
  });
});

describe("the owner's rail", () => {
  it('is filtered the same way, so an owner holding every permission sees Employees, Reports and Payouts', () => {
    const owner: DealerPermissions = { ...employee(true), isOwner: true, canManageStaff: true };

    expect(routes(railFor(DEALER_NAV, owner))).toEqual(
      expect.arrayContaining(['/dealer/employees', '/dealer/reports', '/dealer/payouts']),
    );
  });
});
