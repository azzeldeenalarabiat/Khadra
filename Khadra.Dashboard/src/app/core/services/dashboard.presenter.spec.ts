import { describe, expect, it } from 'vitest';
import { ActivityEntry, AttentionItem, AttentionQueue, BookingTrend } from '../models/dashboard.api';
import { busiestDay, toActivityRows, toQueueItems, toTrendBars } from './dashboard.presenter';
import { AR } from '../i18n/ar';
import { EN } from '../i18n/en';
import { resolveMessage } from '../i18n/resolve';
import { Translate } from './dashboard.presenter';

/** Resolves real English, so these assertions still read as the words an admin sees. */
const t: Translate = (key, params) => resolveMessage(EN[key], params, 'en-GB', false) ?? key;

/**
 * Where a "Requires attention" row leads.
 *
 * The queue's whole purpose is that an admin can act on the item in front of them, and every button
 * used to point at the list: six rows naming six different tickets, all six landing on /disputes,
 * where the ticket had to be found again. The API sends the ids; these pin that they are used, and
 * that a row standing for more than one record still opens the list, because there is no single
 * record for it to open.
 */
describe('toQueueItems', () => {
  const now = Date.parse('2026-09-04T09:00:00Z');

  const item = (over: Partial<AttentionItem>): AttentionItem => ({
    id: 'dispute:t1',
    kind: 'DisputeOverdue',
    severity: 'Overdue',
    count: 1,
    subjectIds: ['t1'],
    subtitle: 'Zarqa Auto Lease · KH-XE5NTW3U',
    description: 'Deposit forfeited after a no-show.',
    slaStartedAt: '2026-09-01T09:00:00Z',
    slaDeadlineAt: '2026-09-03T09:00:00Z',
    isOverdue: true,
    ...over,
  });

  const queue = (...items: AttentionItem[]): AttentionQueue => ({
    slaHours: 48,
    openCount: items.length,
    overdueCount: items.filter((row) => row.isOverdue).length,
    items,
    generatedAt: '2026-09-04T09:00:00Z',
  });

  it('opens the ticket a dispute row is about, not the queue it sits in', () => {
    const [row] = toQueueItems(queue(item({})), now, t);

    expect(row.route).toBe('/disputes/t1');
    expect(row.action).toBe('Resolve');
  });

  it('opens the application a single dealer row is about', () => {
    const [row] = toQueueItems(
      queue(item({ kind: 'DealerApplicationsAtRisk', subjectIds: ['d9'], id: 'dealer:d9' })),
      now,
      t,
    );

    expect(row.route).toBe('/dealers/d9');
    expect(row.action).toBe('Review');
  });

  /** Several applications at risk is one row about several records; only the list can show them. */
  it('falls back to the list when a row stands for more than one record', () => {
    const [row] = toQueueItems(
      queue(
        item({
          kind: 'DealerApplicationsAtRisk',
          count: 3,
          subjectIds: ['d1', 'd2', 'd3'],
          id: 'dealer:atrisk',
        }),
      ),
      now,
      t,
    );

    expect(row.route).toBe('/dealers');
  });

  /** A kind that ships before the frontend knows it still renders; it just cannot deep-link. */
  it('renders an unknown kind without inventing a destination for it', () => {
    const [row] = toQueueItems(queue(item({ kind: 'PayoutOverdue', subjectIds: ['p1'] })), now, t);

    expect(row.route).toBe('/dashboard');
    expect(row.action).toBe('Open');
  });

  it('carries the API id, so two identical-looking rows are still two rows', () => {
    const twin = {
      subtitle: 'Al-Nadeem Rentals · KH-1',
      description: 'Fuel level on return is disputed.',
      slaStartedAt: '2026-09-03T09:00:00Z',
      slaDeadlineAt: '2026-09-05T09:00:00Z',
      isOverdue: false,
    };
    const rows = toQueueItems(
      queue(
        item({ id: 'dispute:a', subjectIds: ['a'], ...twin }),
        item({ id: 'dispute:b', subjectIds: ['b'], ...twin }),
      ),
      now,
      t,
    );

    expect(rows.map((row) => row.id)).toEqual(['dispute:a', 'dispute:b']);
    expect(rows.map((row) => row.route)).toEqual(['/disputes/a', '/disputes/b']);
  });

  /** No id to follow is not a reason to render a broken link. */
  it('opens the list when a row carries no subject at all', () => {
    const [row] = toQueueItems(queue(item({ subjectIds: [] })), now, t);

    expect(row.route).toBe('/disputes');
  });
});

/**
 * The fortnight chart.
 *
 * It used to return bare heights, and a reader had no way to tell it apart from decoration: no
 * dates, no counts, no scale, and a quiet day rendered as nothing at all so a fortnight showed
 * twelve bars. The figures were real the whole time and looked invented, which is the one thing this
 * console must not do. These pin that every bar carries the day and the count it was drawn from.
 */
/**
 * The money rows (payments Phase 4b): they have no clock — nobody froze a deadline for money owed
 * back — so they say how long they have waited instead of counting down, carry no meter, and open the
 * list or the booking they are about.
 */
describe('toQueueItems, the money rows', () => {
  const now = Date.parse('2026-09-26T12:00:00Z');
  const money = (over: Partial<AttentionItem>): AttentionItem => ({
    id: 'refunds-failed',
    kind: 'RefundFailed',
    severity: 'Warning',
    count: 2,
    subjectIds: ['r1', 'r2'],
    subtitle: 'KH-AAA11111 · KH-BBB22222',
    description: null,
    slaStartedAt: '2026-09-24T12:00:00Z',
    slaDeadlineAt: null,
    isOverdue: false,
    ...over,
  });
  const queue = (...items: AttentionItem[]): AttentionQueue => ({
    slaHours: 48,
    openCount: items.length,
    overdueCount: 0,
    items,
    generatedAt: '2026-09-26T12:00:00Z',
  });

  it('words refused refunds as money still owed, with how long they have waited and no meter', () => {
    const [row] = toQueueItems(queue(money({})), now, t);

    expect(row.title).toBe('2 refunds refused — still owed');
    expect(row.severity).toBe('Needs a look');
    expect(row.sla).toBe('Waiting 2d');
    expect(row.hasClock).toBe(false);
    expect(row.percent).toBeNull();
    expect(row.route).toBe('/payments/refunds');
    expect(row.entity).toBe('KH-AAA11111 · KH-BBB22222');
  });

  it('opens the captures being refunded as a filtered payments list', () => {
    const [row] = toQueueItems(
      queue(money({ id: 'orphaned-captures', kind: 'OrphanedCaptureOwed', severity: 'Info', count: 1, subjectIds: ['p1'] })),
      now,
      t,
    );

    expect(row.title).toBe('1 capture being refunded — it could not be applied');
    expect(row.severity).toBe('Watching');
    expect(row.route).toBe('/payments');
    expect(row.queryParams).toEqual({ status: 'Orphaned' });
  });

  it('opens the booking a held deposit belongs to', () => {
    const [row] = toQueueItems(
      queue(money({ id: 'deposit:b1', kind: 'DepositAwaitingDecision', severity: 'Info', count: 1, subjectIds: ['b1'], subtitle: 'Petra Wheels · KH-CCC33333' })),
      now,
      t,
    );

    expect(row.title).toBe('Deposit held for a customer penalty — no dispute was opened');
    expect(row.route).toBe('/bookings/b1');
    expect(row.queryParams).toBeUndefined();
  });

  it('keeps a clock and a meter on the rows that have a deadline', () => {
    const [row] = toQueueItems(
      queue(money({ id: 'dispute:t1', kind: 'DisputeOpen', severity: 'Info', count: 1, subjectIds: ['t1'], slaDeadlineAt: '2026-09-27T12:00:00Z' })),
      now,
      t,
    );

    expect(row.hasClock).toBe(true);
    expect(row.percent).not.toBeNull();
  });
});

describe('toTrendBars', () => {
  const trend = (counts: readonly number[], changePercent: number | null = 0): BookingTrend => ({
    generatedAt: '2026-09-05T00:00:00Z',
    from: '2026-09-01',
    to: '2026-09-01',
    changePercent,
    points: counts.map((count, index) => ({
      date: `2026-09-${String(index + 1).padStart(2, '0')}`,
      count,
    })),
  });

  it('draws a bar for every day, including the ones with no bookings', () => {
    const bars = toTrendBars(trend([5, 0, 3]), t, 'en-GB');

    expect(bars).toHaveLength(3);
    expect(bars.map((bar) => bar.count)).toEqual([5, 0, 3]);
    // The quiet day is still a day. Its height is zero; its bar is not missing.
    expect(bars[1].height).toBe(0);
    expect(bars[1].date).toBe('2026-09-02');
  });

  it('scales every bar against the busiest day', () => {
    const bars = toTrendBars(trend([10, 5]), t, 'en-GB');

    expect(bars[0].height).toBe(88);
    expect(bars[1].height).toBe(44);
  });

  it('keeps a single booking visible rather than rounding it away', () => {
    const bars = toTrendBars(trend([100, 1]), t, 'en-GB');

    expect(bars[1].height).toBeGreaterThanOrEqual(6);
  });

  it('says what each bar is, so a figure can be checked against the bookings', () => {
    const bars = toTrendBars(trend([17, 1]), t, 'en-GB');

    expect(bars[0].label).toContain('17 bookings');
    expect(bars[1].label).toContain('1 booking');
    expect(bars[1].label).not.toContain('1 bookings');
  });

  /** A fortnight of daily ticks would be a smear, so only some bars are labelled. */
  it('labels the ends of the window and thins the rest', () => {
    const bars = toTrendBars(trend(Array.from({ length: 14 }, () => 1)), t, 'en-GB');
    const labelled = bars.filter((bar) => bar.tick !== '');

    expect(bars[0].tick).not.toBe('');
    expect(bars[13].tick).not.toBe('');
    expect(labelled.length).toBeLessThan(bars.length);
  });

  it('draws no bars at all rather than a flat row when nothing was booked', () => {
    const bars = toTrendBars(trend([0, 0, 0]), t, 'en-GB');

    expect(bars.every((bar) => bar.height === 0)).toBe(true);
    expect(busiestDay(trend([0, 0, 0]))).toBe(0);
  });

  it('reports the busiest day, which is what the scale is stated against', () => {
    expect(busiestDay(trend([4, 19, 7]))).toBe(19);
  });
});

/**
 * The activity strip, in both languages (owner, 2026-09-27).
 *
 * The owner found "Azzeldeen Al-Arabiat حسم النزاع Dispute on KH-NY8AHLNK": a dispute's stored label
 * was an English sentence, which the table can never have rewritten, glued after an Arabic verb. Each
 * line is now one sentence with the actor and the subject as parameters, and a dispute's subject is
 * the booking reference the server reads through the ticket.
 */
describe('toActivityRows', () => {
  const now = Date.parse('2026-09-27T09:00:00Z');
  // Resolved the way the console resolves Arabic: every value isolated from the words around it.
  const tAr: Translate = (key, params) =>
    resolveMessage(AR[key], params, 'ar-JO-u-nu-latn', true) ?? key;
  const visible = (text: string) => text.replace(/[⁨⁩]/g, '');

  const entry = (over: Partial<ActivityEntry>): ActivityEntry => ({
    id: 'e1',
    occurredAt: '2026-09-27T08:00:00Z',
    actorName: 'Azzeldeen Al-Arabiat',
    action: 'DisputeResolved',
    entityType: 'Dispute',
    subjectLabel: 'Dispute on KH-NY8AHLNK',
    entityId: '0198f2c4-5b7e-7a10-9c3d-2e4f6a8b0c1d',
    bookingReference: 'KH-NY8AHLNK',
    ...over,
  });

  const english = (over: Partial<ActivityEntry>) =>
    toActivityRows([entry(over)], now, t, 'en-GB')[0].text;
  const arabic = (over: Partial<ActivityEntry>) =>
    visible(toActivityRows([entry(over)], now, tAr, 'ar-JO-u-nu-latn')[0].text);

  it('words a dispute from its booking, not from the English label it was stored with', () => {
    expect(english({})).toBe('Azzeldeen Al-Arabiat resolved the dispute on booking KH-NY8AHLNK');
    expect(arabic({})).toBe('حُسم النزاع على الحجز KH-NY8AHLNK من قِبل Azzeldeen Al-Arabiat');
    expect(arabic({})).not.toContain('Dispute');
  });

  it('reads the same for a dispute stored with the bare reference', () => {
    expect(arabic({ subjectLabel: 'KH-NY8AHLNK' })).toBe(
      'حُسم النزاع على الحجز KH-NY8AHLNK من قِبل Azzeldeen Al-Arabiat',
    );
  });

  it('keeps each Latin value in its own isolate inside an Arabic line', () => {
    const raw = toActivityRows([entry({})], now, tAr, 'ar-JO-u-nu-latn')[0].text;

    expect(raw).toContain('⁨KH-NY8AHLNK⁩');
    expect(raw).toContain('⁨Azzeldeen Al-Arabiat⁩');
  });

  it('names a customer by the short reference, and says "customer" once', () => {
    const suspended: Partial<ActivityEntry> = {
      action: 'CustomerSuspended',
      entityType: 'Customer',
      subjectLabel: 'Customer 0198abcd',
      entityId: '0198abcd-1234-7def-8abc-0123456789ab',
      bookingReference: null,
      actorName: 'Omar Haddad',
    };

    expect(english(suspended)).toBe('Omar Haddad suspended customer 0198abcd');
    expect(arabic(suspended)).toBe('أُوقف حساب العميل 0198abcd من قِبل Omar Haddad');
  });

  it('leaves the English of every other line as it read before', () => {
    const approved: Partial<ActivityEntry> = {
      action: 'DealerApproved',
      entityType: 'Dealer',
      subjectLabel: 'Aqaba Coast Cars',
      bookingReference: null,
      actorName: 'Rania Haddad',
    };
    const cancelled: Partial<ActivityEntry> = {
      action: 'BookingCancelledByAdmin',
      entityType: 'Booking',
      subjectLabel: 'KH-XE5NTW3U',
      bookingReference: 'KH-XE5NTW3U',
      actorName: 'Rania Haddad',
    };

    expect(english(approved)).toBe('Rania Haddad approved dealer Aqaba Coast Cars');
    expect(arabic(approved)).toBe('اعتُمد المكتب Aqaba Coast Cars من قِبل Rania Haddad');
    expect(english(cancelled)).toBe('Rania Haddad cancelled booking KH-XE5NTW3U');
    expect(arabic(cancelled)).toBe('أُلغي الحجز KH-XE5NTW3U من قِبل Rania Haddad');
  });

  it('words the actions that used to print their raw names', () => {
    const reactivated: Partial<ActivityEntry> = {
      action: 'AdminReactivated',
      entityType: 'AdminUser',
      subjectLabel: 'Omar Haddad',
      bookingReference: null,
    };
    const city: Partial<ActivityEntry> = {
      action: 'LookupCreated',
      entityType: 'City',
      subjectLabel: 'Madaba',
      bookingReference: null,
    };
    const carType: Partial<ActivityEntry> = {
      action: 'LookupRetired',
      entityType: 'CarType',
      subjectLabel: 'Pickup',
      bookingReference: null,
    };

    expect(english(reactivated)).toBe('Azzeldeen Al-Arabiat reactivated admin Omar Haddad');
    expect(arabic(reactivated)).toBe(
      'أُعيد تفعيل حساب المشرف Omar Haddad من قِبل Azzeldeen Al-Arabiat',
    );
    expect(english(city)).toBe('Azzeldeen Al-Arabiat added city Madaba');
    expect(arabic(city)).toBe('أُضيفت المدينة Madaba من قِبل Azzeldeen Al-Arabiat');
    expect(english(carType)).toBe('Azzeldeen Al-Arabiat retired car type Pickup');
    expect(arabic(carType)).toBe('أُوقف نوع السيارة Pickup من قِبل Azzeldeen Al-Arabiat');
  });

  it('spells out an action this build has no sentence for, rather than printing an empty line', () => {
    const unknown: Partial<ActivityEntry> = {
      action: 'PayoutSent',
      entityType: 'Dealer',
      subjectLabel: 'Aqaba Coast Cars',
      bookingReference: null,
    };
    // A lookup action on a list this build does not know is the same case.
    const newList: Partial<ActivityEntry> = { ...unknown, action: 'LookupCreated', entityType: 'Region' };

    expect(english(unknown)).toBe('Azzeldeen Al-Arabiat: Payout sent, Aqaba Coast Cars');
    expect(arabic(unknown)).toBe('Payout sent: Aqaba Coast Cars، من قِبل Azzeldeen Al-Arabiat');
    expect(english(newList)).toBe('Azzeldeen Al-Arabiat: Lookup created, Aqaba Coast Cars');
  });

  it('gives the actions that had no icon one of their own', () => {
    const actions = ['AdminReactivated', 'LookupCreated', 'LookupRenamed', 'LookupRetired', 'LookupRestored'];
    const icons = actions.map((action) => toActivityRows([entry({ action })], now, t, 'en-GB')[0].icon);

    expect(icons).not.toContain('info');
  });
});
