import { describe, expect, it } from 'vitest';
import { AttentionItem, AttentionQueue, BookingTrend } from '../models/dashboard.api';
import { busiestDay, toQueueItems, toTrendBars } from './dashboard.presenter';
import { EN } from '../i18n/en';
import { resolveMessage } from '../i18n/resolve';
import { Translate } from './dashboard.presenter';

/** Resolves real English, so these assertions still read as the words an admin sees. */
const t: Translate = (key, params) =>
  resolveMessage(EN[key], params, 'en-GB', false) ?? key;


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
    const bars = toTrendBars(trend([5, 0, 3]));

    expect(bars).toHaveLength(3);
    expect(bars.map((bar) => bar.count)).toEqual([5, 0, 3]);
    // The quiet day is still a day. Its height is zero; its bar is not missing.
    expect(bars[1].height).toBe(0);
    expect(bars[1].date).toBe('2026-09-02');
  });

  it('scales every bar against the busiest day', () => {
    const bars = toTrendBars(trend([10, 5]));

    expect(bars[0].height).toBe(88);
    expect(bars[1].height).toBe(44);
  });

  it('keeps a single booking visible rather than rounding it away', () => {
    const bars = toTrendBars(trend([100, 1]));

    expect(bars[1].height).toBeGreaterThanOrEqual(6);
  });

  it('says what each bar is, so a figure can be checked against the bookings', () => {
    const bars = toTrendBars(trend([17, 1]));

    expect(bars[0].label).toContain('17 bookings');
    expect(bars[1].label).toContain('1 booking');
    expect(bars[1].label).not.toContain('1 bookings');
  });

  /** A fortnight of daily ticks would be a smear, so only some bars are labelled. */
  it('labels the ends of the window and thins the rest', () => {
    const bars = toTrendBars(trend(Array.from({ length: 14 }, () => 1)));
    const labelled = bars.filter((bar) => bar.tick !== '');

    expect(bars[0].tick).not.toBe('');
    expect(bars[13].tick).not.toBe('');
    expect(labelled.length).toBeLessThan(bars.length);
  });

  it('draws no bars at all rather than a flat row when nothing was booked', () => {
    const bars = toTrendBars(trend([0, 0, 0]));

    expect(bars.every((bar) => bar.height === 0)).toBe(true);
    expect(busiestDay(trend([0, 0, 0]))).toBe(0);
  });

  it('reports the busiest day, which is what the scale is stated against', () => {
    expect(busiestDay(trend([4, 19, 7]))).toBe(19);
  });
});
