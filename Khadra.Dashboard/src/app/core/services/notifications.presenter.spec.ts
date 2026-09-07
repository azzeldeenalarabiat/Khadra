import { describe, expect, it } from 'vitest';
import { AttentionItem, AttentionQueue } from '../models/dashboard.api';
import { DealerDashboard, UpcomingHandover } from '../models/dealer-console.api';
import { toAdminNotifications, toDealerNotifications } from './notifications.presenter';
import { EN } from '../i18n/en';
import { resolveMessage } from '../i18n/resolve';
import { Translate } from './dashboard.presenter';

/** Resolves real English, so these assertions still read as the words an admin sees. */
const t: Translate = (key, params) =>
  resolveMessage(EN[key], params, 'en-GB', false) ?? key;


const NOW = Date.parse('2026-09-05T12:00:00Z');

/**
 * The notifications panel.
 *
 * There is no Notifications context on this platform, so the panel lists what the server already
 * knows is waiting on the reader. That makes one property load-bearing above all others: the badge
 * counts the rows, so it can never advertise work the panel does not then show. The bell it replaced
 * carried a hardcoded "9".
 */
describe('toAdminNotifications', () => {
  const item = (over: Partial<AttentionItem> = {}): AttentionItem => ({
    id: 'dispute:t1',
    kind: 'DisputeOverdue',
    severity: 'Overdue',
    count: 1,
    subjectIds: ['t1'],
    subtitle: 'Zarqa Auto Lease · KH-XE5NTW3U',
    description: 'Deposit forfeited after a no-show.',
    slaStartedAt: '2026-09-01T12:00:00Z',
    slaDeadlineAt: '2026-09-03T12:00:00Z',
    isOverdue: true,
    ...over,
  });

  const queue = (...items: AttentionItem[]): AttentionQueue => ({
    slaHours: 48,
    openCount: items.length,
    overdueCount: items.filter((row) => row.isOverdue).length,
    items,
    generatedAt: '2026-09-05T12:00:00Z',
  });

  it('shows nothing at all before the queue has answered', () => {
    expect(toAdminNotifications(null, NOW, t)).toEqual([]);
  });

  it('opens the record a row is about, not the list it sits in', () => {
    const [row] = toAdminNotifications(queue(item()), NOW, t);

    expect(row.route).toBe('/disputes/t1');
  });

  it('names the record in the detail line, so a row can be recognised', () => {
    const [row] = toAdminNotifications(queue(item()), NOW, t);

    expect(row.detail).toContain('KH-XE5NTW3U');
  });

  /** An overdue row has to look different from one merely approaching its deadline. */
  it('carries the severity through as the row tone', () => {
    const [late] = toAdminNotifications(queue(item({ isOverdue: true, severity: 'Overdue' })), NOW, t);
    const [soon] = toAdminNotifications(
      queue(item({ isOverdue: false, severity: 'Warning', slaDeadlineAt: '2026-09-06T12:00:00Z' })),
      NOW,
      t,
    );

    expect(late.tone).toBe('bad');
    expect(soon.tone).not.toBe('bad');
  });

  it('produces one row per queue item, which is what the badge counts', () => {
    const rows = toAdminNotifications(
      queue(item({ id: 'a', subjectIds: ['a'] }), item({ id: 'b', subjectIds: ['b'] })),
      NOW,
      t,
    );

    expect(rows).toHaveLength(2);
    expect(rows.map((row) => row.id)).toEqual(['a', 'b']);
  });
});

describe('toDealerNotifications', () => {
  const handover = (over: Partial<UpcomingHandover> = {}): UpcomingHandover => ({
    bookingId: 'b1',
    reference: 'KH-AAA111',
    status: 'Approved',
    when: '2026-09-05T15:00:00Z',
    pickupMethod: 'SelfPickup',
    vehicleLabel: 'Toyota Camry 2023',
    customerName: 'Sami Kamal',
    isOverdue: false,
    ...over,
  });

  const dashboard = (over: Partial<DealerDashboard> = {}): DealerDashboard =>
    ({
      businessName: 'Al-Nadeem Rentals',
      canTrade: true,
      bookings: {
        requested: 0,
        oldestRequestedAt: null,
        awaitingDeposit: 0,
        confirmed: 0,
        pickedUp: 0,
        overdueReturns: 0,
      },
      availableVehicles: 1,
      publishedVehicles: 4,
      totalVehicles: 6,
      fleetStatus: [],
      upcomingPickups: [],
      upcomingReturns: [],
      upcomingWindowHours: 48,
      revenueThisMonth: null,
      occupancyPercentLast30Days: null,
      recentActivity: [],
      ...over,
    }) as DealerDashboard;

  it('shows nothing at all before the dashboard has answered', () => {
    expect(toDealerNotifications(null, NOW, t)).toEqual([]);
  });

  /** "0 requests waiting" is not news, and a badge of 0 is worse than no badge. */
  it('says nothing when there is nothing waiting', () => {
    expect(toDealerNotifications(dashboard(), NOW, t)).toEqual([]);
  });

  it('puts what is already late above what is merely due', () => {
    const rows = toDealerNotifications(
      dashboard({
        bookings: {
          requested: 3,
          oldestRequestedAt: '2026-09-01T12:00:00Z',
          awaitingDeposit: 0,
          confirmed: 0,
          pickedUp: 2,
          overdueReturns: 1,
        },
        upcomingPickups: [handover()],
      }),
      NOW,
      t,
    );

    expect(rows[0].id).toBe('overdue-returns');
    expect(rows[0].tone).toBe('bad');
    expect(rows[1].id).toBe('pending-requests');
  });

  it('counts one car and several cars differently', () => {
    const one = toDealerNotifications(
      dashboard({
        bookings: {
          requested: 1,
          oldestRequestedAt: null,
          awaitingDeposit: 0,
          confirmed: 0,
          pickedUp: 0,
          overdueReturns: 1,
        },
      }),
      NOW,
      t,
    );

    expect(one[0].title).toBe('1 car is overdue back');
    expect(one[1].title).toBe('1 booking request is waiting');
  });

  it('carries the oldest request, because that is the one about to expire', () => {
    const [row] = toDealerNotifications(
      dashboard({
        bookings: {
          requested: 2,
          oldestRequestedAt: '2026-09-01T12:00:00Z',
          awaitingDeposit: 0,
          confirmed: 0,
          pickedUp: 0,
          overdueReturns: 0,
        },
      }),
      NOW,
      t,
    );

    expect(row.detail).toContain('4d');
  });

  it('opens the booking a handover is about', () => {
    const rows = toDealerNotifications(
      dashboard({ upcomingReturns: [handover({ bookingId: 'b9', reference: 'KH-ZZZ999' })] }),
      NOW,
      t,
    );

    expect(rows[0].route).toBe('/dealer/bookings/b9');
    expect(rows[0].detail).toContain('KH-ZZZ999');
  });

  /** A delivery and a self-pickup are different jobs, and the row should not imply otherwise. */
  it('distinguishes a delivery from a collection', () => {
    const [delivery] = toDealerNotifications(
      dashboard({ upcomingPickups: [handover({ pickupMethod: 'Delivery' })] }),
      NOW,
      t,
    );
    const [collect] = toDealerNotifications(
      dashboard({ upcomingPickups: [handover({ pickupMethod: 'SelfPickup' })] }),
      NOW,
      t,
    );

    expect(delivery.icon).toBe('moped');
    expect(collect.icon).toBe('map-pin');
  });

  it('marks an overdue handover as overdue rather than upcoming', () => {
    const [row] = toDealerNotifications(
      dashboard({ upcomingReturns: [handover({ isOverdue: true })] }),
      NOW,
      t,
    );

    expect(row.tone).toBe('bad');
  });

  /** The badge is `rows.length`, so this is the property the count depends on. */
  it('produces exactly one row per thing to do', () => {
    const rows = toDealerNotifications(
      dashboard({
        bookings: {
          requested: 5,
          oldestRequestedAt: '2026-09-04T12:00:00Z',
          awaitingDeposit: 0,
          confirmed: 0,
          pickedUp: 0,
          overdueReturns: 2,
        },
        upcomingPickups: [handover({ bookingId: 'p1' }), handover({ bookingId: 'p2' })],
        upcomingReturns: [handover({ bookingId: 'r1' })],
      }),
      NOW,
      t,
    );

    // Two counts collapse to one row each; the five handovers-in-window are listed individually.
    expect(rows).toHaveLength(5);
    expect(new Set(rows.map((row) => row.id)).size).toBe(5);
  });
});
