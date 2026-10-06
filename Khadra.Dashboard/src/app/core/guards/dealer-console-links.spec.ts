// Loaded before anything Angular: the route table imports guards and components with partially compiled decorators.
import '@angular/compiler';
import { Route } from '@angular/router';
import { describe, expect, it } from 'vitest';
import { routes } from '../../app.routes';

/**
 * The screens both dealer consoles show (E2E F79). The bookings list, a booking, a dispute, the fleet and a car are
 * mounted under `/dealer` for the owner and under `/employee` for staff, and so is the bell in every header. A written
 * `/dealer/...` sends an employee into the owner's console, whose guard returns them to their own dashboard: staff
 * could not open a booking from their own list, a car from the fleet, or the dispute they had just opened. Every link
 * these screens draw goes through `dealerConsoleRoot` instead.
 */

/** The child paths a console mounts, read from the route table itself. */
function mounted(name: 'dealer' | 'employee'): readonly string[] {
  const find = (list: readonly Route[]): Route | undefined => {
    for (const route of list) {
      if (route.path === name) return route;
      const inner = route.children ? find(route.children) : undefined;
      if (inner) return inner;
    }
    return undefined;
  };
  return (find(routes)?.children ?? []).map((route) => route.path ?? '');
}

/** Every place the shared screens and the bell link to, as `[root(), …]`. */
const LINKED = [
  'dashboard',
  'bookings',
  'bookings/:bookingId',
  'disputes/:ticketId',
  'fleet',
  'fleet/:vehicleId',
  'payouts',
];

/** The screens both consoles mount, and the bell every console's header carries. */
const SHARED = [
  'features/dealer/dealer-gate.component.html',
  'features/dealer/dealer-gate.component.ts',
  'features/dealer/dealer-bookings.component.html',
  'features/dealer/dealer-bookings.component.ts',
  'features/dealer/booking-detail.component.html',
  'features/dealer/booking-detail.component.ts',
  'features/dealer/dealer-dispute.component.html',
  'features/dealer/dealer-dispute.component.ts',
  'features/fleet/fleet-list.component.html',
  'features/fleet/fleet-list.component.ts',
  'features/fleet/vehicle-detail.component.html',
  'features/fleet/vehicle-detail.component.ts',
  'features/dealer/dealer-reports.component.html',
  'features/dealer/dealer-reports.component.ts',
  'features/dealer/dealer-payouts.component.html',
  'features/dealer/dealer-payouts.component.ts',
  'layout/notifications-menu.component.html',
  'layout/notifications-menu.component.ts',
  'core/services/notifications.presenter.ts',
];

/**
 * The only lines in those files that may still name the owner's console, each for a reason: the owner's own actions,
 * which render for the owner alone (adding and editing a car behind `canAdd` / `canManage`, false for staff; the gate's
 * application, which only an owner without one sees, and its dealer page behind `isOwner()`); the gate choosing
 * between the two consoles itself; a check on the address; and a type naming both consoles.
 */
const OWNER_ONLY: Record<string, readonly string[]> = {
  'features/dealer/dealer-gate.component.html': [
    `<a class="btn btn-primary btn-md" routerLink="/dealer/apply">{{ t('gate.notSubmitted.submit') }}</a>`,
    `<a class="btn btn-primary btn-md" routerLink="/dealer/profile">{{ t('gate.openDealerPage') }}</a>`,
  ],
  'features/dealer/dealer-gate.component.ts': [
    `this.session.user()?.role === 'DealerEmployee' ? '/employee/bookings' : '/dealer/bookings',`,
    `protected readonly applying = computed(() => this.url().startsWith('/dealer/apply'));`,
  ],
  'features/fleet/fleet-list.component.html': [
    `<a class="btn btn-primary btn-md" routerLink="/dealer/fleet/new">`,
    `<a class="btn btn-primary btn-sm" routerLink="/dealer/fleet/new">{{ t('fleetList.addACar') }}</a>`,
    `[routerLink]="['/dealer/fleet', car.vehicleId, 'edit']"`,
  ],
  'features/fleet/vehicle-detail.component.html': [
    `<a class="btn btn-primary btn-md" [routerLink]="['/dealer/fleet', c.vehicleId, 'edit']">`,
  ],
  'core/services/notifications.presenter.ts': [`root: '/dealer' | '/employee',`],
};

/** The lines of a source file that write a `/dealer` route, comments left out. */
function dealerLines(source: string): string[] {
  return source
    .replace(/<!--[\s\S]*?-->/g, '')
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => !line.startsWith('//'))
    .filter((line) => /['"`]\/dealer(\/|['"`])/.test(line));
}

/**
 * The sources, read from disk. The specs run under Node but carry no Node type definitions, so its file API is reached
 * at run time rather than imported.
 */
interface FileSystem {
  readFileSync(path: string, encoding: 'utf8'): string;
}
const nodeModule = (name: string): Promise<FileSystem> => import(/* @vite-ignore */ name);
const projectRoot = (globalThis as unknown as { process: { cwd(): string } }).process.cwd();
const read = async (file: string) =>
  (await nodeModule('node:fs')).readFileSync(`${projectRoot}/src/app/${file}`, 'utf8');

describe('the screens both dealer consoles show (E2E F79)', () => {
  it.each(['dealer', 'employee'] as const)(
    'mounts every place they link to in the %s console',
    (console) => {
      const paths = mounted(console);
      for (const place of LINKED) expect(paths, `/${console}/${place}`).toContain(place);
    },
  );

  it('links through the console root, naming the owner console only where the owner alone looks', async () => {
    const found: Record<string, string[]> = {};
    for (const file of SHARED) {
      const lines = dealerLines(await read(file));
      if (lines.length > 0) found[file] = lines;
    }
    expect(found).toEqual(OWNER_ONLY);
  });

  it('draws every record link of the shared screens through the root', async () => {
    for (const file of SHARED.filter(
      (name) => name.endsWith('.component.ts') && !name.includes('dealer-gate'),
    )) {
      const source = await read(file);
      if (!/RouterLink|router\.navigate/.test(source)) continue;
      expect(source, file).toContain('dealerConsoleRoot(this.session.user())');
    }
  });
});
