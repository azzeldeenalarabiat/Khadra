import { describe, expect, it } from 'vitest';
import { addDays, instantToWallClock, isWallClock, wallClockToInstant } from './zoned-time';

describe('zoned time', () => {
  it('reads a wall-clock time in Amman as the instant it names', () => {
    // Amman is UTC+3 all year.
    expect(wallClockToInstant({ date: '2026-09-25', time: '10:00' }, 'Asia/Amman')?.toISOString()).toBe(
      '2026-09-25T07:00:00.000Z',
    );
  });

  it('crosses midnight backwards when the zone is ahead of UTC', () => {
    expect(wallClockToInstant({ date: '2026-09-25', time: '01:30' }, 'Asia/Amman')?.toISOString()).toBe(
      '2026-09-24T22:30:00.000Z',
    );
  });

  it('honours a zone that changes its offset during the year', () => {
    expect(wallClockToInstant({ date: '2026-01-15', time: '12:00' }, 'Europe/London')?.toISOString()).toBe(
      '2026-01-15T12:00:00.000Z',
    );
    expect(wallClockToInstant({ date: '2026-07-15', time: '12:00' }, 'Europe/London')?.toISOString()).toBe(
      '2026-07-15T11:00:00.000Z',
    );
  });

  it('turns an instant back into the wall clock it shows there', () => {
    expect(instantToWallClock(new Date('2026-09-24T22:30:00Z'), 'Asia/Amman')).toEqual({ date: '2026-09-25', time: '01:30' });
  });

  it('refuses anything that is not a date and a time', () => {
    expect(wallClockToInstant({ date: '2026-9-5', time: '10:00' }, 'Asia/Amman')).toBeNull();
    expect(wallClockToInstant({ date: '2026-09-25', time: '24:00' }, 'Asia/Amman')).toBeNull();
    expect(isWallClock({ date: '2026-09-25', time: '10:00' })).toBe(true);
    expect(isWallClock({ date: '2026-09-25' })).toBe(false);
  });

  it('adds calendar days without a zone moving them', () => {
    expect(addDays('2026-09-30', 1)).toBe('2026-10-01');
    expect(addDays('2026-12-31', 3)).toBe('2027-01-03');
  });
});
