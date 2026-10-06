import { describe, expect, it } from 'vitest';
import { AttentionItem, AttentionQueue } from '../models/dashboard.api';
import { DealerDashboard, UpcomingHandover } from '../models/dealer-console.api';
import { NotificationItem, NotificationKind } from '../models/notifications.api';
import { notificationSentence, toAdminNotifications, toDealerNotifications } from './notifications.presenter';
import { AR } from '../i18n/ar';
import { EN } from '../i18n/en';
import { resolveMessage } from '../i18n/resolve';
import { Translate } from './dashboard.presenter';

/** Resolves real English, so these assertions still read as the words an admin sees. */
const t: Translate = (key, params) => resolveMessage(EN[key], params, 'en-GB', false) ?? key;

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
    const [late] = toAdminNotifications(
      queue(item({ isOverdue: true, severity: 'Overdue' })),
      NOW,
      t,
    );
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

  /**
   * Money with no clock (payments Phase 4b): a refused refund, and the offices' money the ledger holds back
   * (payments Phase 8), need a look and ring the bell; a capture being refunded is watched, and stays on the
   * dashboard.
   */
  it('rings for money a person has to look at and leaves watched money to the dashboard', () => {
    const noClock = { slaDeadlineAt: null, isOverdue: false, description: null };
    const rows = toAdminNotifications(
      queue(
        item({ id: 'refunds-failed', kind: 'RefundFailed', severity: 'Warning', ...noClock }),
        item({ id: 'orphaned-captures', kind: 'OrphanedCaptureOwed', severity: 'Info', ...noClock }),
        item({ id: 'payables-on-hold', kind: 'PayablesOnHold', severity: 'Warning', ...noClock }),
        item({ id: 'dispute:t2', kind: 'DisputeOpen', severity: 'Info', isOverdue: false, slaDeadlineAt: '2026-09-06T12:00:00Z' }),
      ),
      NOW,
      t,
    );

    expect(rows.map((row) => row.id)).toEqual(['refunds-failed', 'payables-on-hold', 'dispute:t2']);
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
        earliestDecisionDeadline: null,
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
    expect(toDealerNotifications(null, NOW, t, 'en-GB')).toEqual([]);
  });

  /** "0 requests waiting" is not news, and a badge of 0 is worse than no badge. */
  it('says nothing when there is nothing waiting', () => {
    expect(toDealerNotifications(dashboard(), NOW, t, 'en-GB')).toEqual([]);
  });

  it('puts what is already late above what is merely due', () => {
    const rows = toDealerNotifications(
      dashboard({
        bookings: {
          requested: 3,
          oldestRequestedAt: '2026-09-01T12:00:00Z',
          earliestDecisionDeadline: null,
          awaitingDeposit: 0,
          confirmed: 0,
          pickedUp: 2,
          overdueReturns: 1,
        },
        upcomingPickups: [handover()],
      }),
      NOW,
      t,
      'en-GB',
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
          earliestDecisionDeadline: null,
          awaitingDeposit: 0,
          confirmed: 0,
          pickedUp: 0,
          overdueReturns: 1,
        },
      }),
      NOW,
      t,
      'en-GB',
    );

    expect(one[0].title).toBe('1 car is overdue back');
    expect(one[1].title).toBe('1 booking request is waiting');
  });

  /**
   * The oldest request, and when the first one expires (Wave 3, F24): the server's moment, which need not be the
   * oldest's. The line used to say a request expires when its rental date arrives.
   */
  it('names when the first request expires, beside the oldest', () => {
    const [row] = toDealerNotifications(
      dashboard({
        bookings: {
          requested: 2,
          oldestRequestedAt: '2026-09-01T12:00:00Z',
          earliestDecisionDeadline: '2026-09-05T17:00:00Z',
          awaitingDeposit: 0,
          confirmed: 0,
          pickedUp: 0,
          overdueReturns: 0,
        },
      }),
      NOW,
      t,
      'en-GB',
    );

    expect(row.detail).toBe('Oldest 4 days ago. The next one expires in 5 hr unless it is answered.');
  });

  it('states the rule, with no moment, when the server names none', () => {
    const [row] = toDealerNotifications(
      dashboard({
        bookings: {
          requested: 1,
          oldestRequestedAt: '2026-09-01T12:00:00Z',
          earliestDecisionDeadline: null,
          awaitingDeposit: 0,
          confirmed: 0,
          pickedUp: 0,
          overdueReturns: 0,
        },
      }),
      NOW,
      t,
      'en-GB',
    );

    expect(row.detail).toBe('An unanswered request expires at its answer deadline.');
  });

  it('opens the booking a handover is about', () => {
    const rows = toDealerNotifications(
      dashboard({ upcomingReturns: [handover({ bookingId: 'b9', reference: 'KH-ZZZ999' })] }),
      NOW,
      t,
      'en-GB',
    );

    expect(rows[0].route).toBe('/dealer/bookings/b9');
    expect(rows[0].detail).toContain('KH-ZZZ999');
  });

  /** The bug this pins: an upcoming handover read "Just now", because only the past was counted. */
  it('says when an upcoming handover is, counting forwards', () => {
    const [row] = toDealerNotifications(
      dashboard({ upcomingPickups: [handover({ when: '2026-09-05T15:00:00Z' })] }),
      NOW,
      t,
      'en-GB',
    );

    expect(row.when).toBe('in 3 hr');
  });

  /** A car or an account that is gone arrives as null, and the row still names what it can. */
  it('words a vehicle and a customer that no longer resolve', () => {
    const [row] = toDealerNotifications(
      dashboard({ upcomingReturns: [handover({ vehicleLabel: null, customerName: null })] }),
      NOW,
      t,
      'en-GB',
    );

    expect(row.title).toBe('Return — Vehicle no longer listed');
    expect(row.detail).toBe('Customer account closed · KH-AAA111');
  });

  /** A delivery and a self-pickup are different jobs, and the row should not imply otherwise. */
  it('distinguishes a delivery from a collection', () => {
    const [delivery] = toDealerNotifications(
      dashboard({ upcomingPickups: [handover({ pickupMethod: 'Delivery' })] }),
      NOW,
      t,
      'en-GB',
    );
    const [collect] = toDealerNotifications(
      dashboard({ upcomingPickups: [handover({ pickupMethod: 'SelfPickup' })] }),
      NOW,
      t,
      'en-GB',
    );

    expect(delivery.icon).toBe('moped');
    expect(collect.icon).toBe('map-pin');
  });

  it('marks an overdue handover as overdue rather than upcoming', () => {
    const [row] = toDealerNotifications(
      dashboard({ upcomingReturns: [handover({ isOverdue: true })] }),
      NOW,
      t,
      'en-GB',
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
          earliestDecisionDeadline: null,
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
      'en-GB',
    );

    // Two counts collapse to one row each; the five handovers-in-window are listed individually.
    expect(rows).toHaveLength(5);
    expect(new Set(rows.map((row) => row.id)).size).toBe(5);
  });
});

describe('notificationSentence (pre-launch item 218)', () => {
  // Real Arabic, without the direction isolates the resolver wraps each value in.
  const ar: Translate = (key, params) =>
    (resolveMessage(AR[key], params, 'ar-JO-u-nu-latn', true) ?? key).replace(/[\u2068\u2069]/g, '');
  const item = (over: Partial<NotificationItem> = {}): NotificationItem => ({
    notificationId: 'n-1',
    kind: 'BookingApproved',
    subjectId: 'b-1',
    subjectReference: 'KH-ABCD1234',
    actorName: 'Rana Haddad',
    isMine: false,
    occurredAt: '2026-09-30T09:00:00Z',
    readAt: null,
    isRead: false,
    ...over,
  });

  it("says what the reader did in a sentence of its own, never «أنت» dropped into the actor's place", () => {
    expect(notificationSentence(item({ isMine: true }), ar)).toBe('أنت من قبِل KH-ABCD1234');
    expect(notificationSentence(item({ isMine: true }), t)).toBe('You approved KH-ABCD1234');

    const kinds: readonly NotificationKind[] = ['BookingApproved', 'BookingRejected', 'BookingPickedUp', 'BookingReturned', 'StaffReactivated'];
    // A kind this build does not know falls back to "updated", and that fallback reads right for the reader too.
    for (const kind of [...kinds, 'SomethingNewer' as NotificationKind]) {
      const arabic = notificationSentence(item({ kind, isMine: true }), ar);
      expect(arabic.startsWith('أنت من ')).toBe(true);
      expect(notificationSentence(item({ kind, isMine: true }), t)).toMatch(/^You /);
    }
  });

  it('names a colleague as it always has', () => {
    expect(notificationSentence(item(), ar)).toBe('قبل Rana Haddad KH-ABCD1234');
    expect(notificationSentence(item(), t)).toBe('Rana Haddad approved KH-ABCD1234');
    expect(notificationSentence(item({ kind: 'BookingPickedUp', subjectReference: null }), t)).toBe(
      'Rana Haddad recorded the pickup for a booking',
    );
  });
});
