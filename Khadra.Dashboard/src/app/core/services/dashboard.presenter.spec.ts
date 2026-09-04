import { describe, expect, it } from 'vitest';
import { AttentionItem, AttentionQueue } from '../models/dashboard.api';
import { toQueueItems } from './dashboard.presenter';

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
    const [row] = toQueueItems(queue(item({})), now);

    expect(row.route).toBe('/disputes/t1');
    expect(row.action).toBe('Resolve');
  });

  it('opens the application a single dealer row is about', () => {
    const [row] = toQueueItems(
      queue(item({ kind: 'DealerApplicationsAtRisk', subjectIds: ['d9'], id: 'dealer:d9' })),
      now,
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
    );

    expect(row.route).toBe('/dealers');
  });

  /** A kind that ships before the frontend knows it still renders; it just cannot deep-link. */
  it('renders an unknown kind without inventing a destination for it', () => {
    const [row] = toQueueItems(queue(item({ kind: 'PayoutOverdue', subjectIds: ['p1'] })), now);

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
    );

    expect(rows.map((row) => row.id)).toEqual(['dispute:a', 'dispute:b']);
    expect(rows.map((row) => row.route)).toEqual(['/disputes/a', '/disputes/b']);
  });

  /** No id to follow is not a reason to render a broken link. */
  it('opens the list when a row carries no subject at all', () => {
    const [row] = toQueueItems(queue(item({ subjectIds: [] })), now);

    expect(row.route).toBe('/disputes');
  });
});
