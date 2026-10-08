import { describe, expect, it } from 'vitest';
import { EN } from '../../core/i18n/en';
import { resolveMessage } from '../../core/i18n/resolve';
import { VehicleCalendarDay } from '../../core/models/fleet.api';
import { calendarCells, shiftCalendarMonth } from './vehicle-calendar.presenter';

const t = (key: keyof typeof EN) => resolveMessage(EN[key], undefined, 'en-GB', false) ?? key;

const day = (date: string, over: Partial<VehicleCalendarDay> = {}): VehicleCalendarDay => ({
  date,
  status: null,
  bookingId: null,
  reference: null,
  turnaround: false,
  ...over,
});

/** Pre-launch item 54: the server cuts the days; the screen words them. */
describe('calendarCells', () => {
  it('numbers each cell by its calendar date, never by the browser clock', () => {
    const cells = calendarCells([day('2026-10-01'), day('2026-10-31')], 'Active', t);
    expect(cells.map((cell) => cell.n)).toEqual([1, 31]);
    expect(cells.every((cell) => cell.tone === 'ok')).toBe(true);
  });

  it('marks the turnaround before a rental as the car being prepared, linked to that booking', () => {
    const [cell] = calendarCells(
      [
        day('2026-10-05', {
          status: 'Confirmed',
          bookingId: 'b1',
          reference: 'KH-AAAA1111',
          turnaround: true,
        }),
      ],
      'Active',
      t,
    );
    expect(cell).toEqual({ n: 5, tag: 'Preparing', tone: 'warn', bookingId: 'b1' });
  });

  it('words each holding status, and a confirmed booking by its reference', () => {
    const cells = calendarCells(
      [
        day('2026-10-01', { status: 'PickedUp', bookingId: 'a' }),
        day('2026-10-02', { status: 'Requested', bookingId: 'b' }),
        day('2026-10-03', { status: 'Approved', bookingId: 'c' }),
        day('2026-10-04', { status: 'Confirmed', bookingId: 'd', reference: 'KH-DDDD4444' }),
      ],
      'Active',
      t,
    );
    expect(cells.map((cell) => cell.tone)).toEqual(['accent', 'bad', 'bad', 'warn']);
    expect(cells[3].tag).toBe('KH-DDDD4444');
  });

  it("reads a day nothing holds by the car's own state", () => {
    expect(calendarCells([day('2026-10-01')], 'Maintenance', t)[0].tone).toBe('dim');
    expect(calendarCells([day('2026-10-01')], 'Hidden', t)[0].tone).toBe('dim');
    expect(calendarCells([day('2026-10-01')], 'Active', t)[0].tone).toBe('ok');
  });
});

describe('shiftCalendarMonth', () => {
  it('crosses the year in both directions', () => {
    expect(shiftCalendarMonth({ year: 2026, month: 12 }, 1)).toEqual({ year: 2027, month: 1 });
    expect(shiftCalendarMonth({ year: 2026, month: 1 }, -1)).toEqual({ year: 2025, month: 12 });
    expect(shiftCalendarMonth({ year: 2026, month: 10 }, 0)).toEqual({ year: 2026, month: 10 });
  });
});
