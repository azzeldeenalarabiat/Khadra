import { describe, expect, it } from 'vitest';
import { GalleryDaySchedule } from '../../core/api/catalogue.api';
import { groupHours, weekdayIn } from './hours';

const open = (day: string, opens = '09:00:00', closes = '18:00:00'): GalleryDaySchedule => ({ day, isClosed: false, opens, closes });
const shut = (day: string): GalleryDaySchedule => ({ day, isClosed: true, opens: null, closes: null });

describe('opening hours, grouped', () => {
  it('merges neighbouring days that keep the same hours', () => {
    const runs = groupHours([
      open('Sunday'), open('Monday'), open('Tuesday'), open('Wednesday'), open('Thursday'),
      shut('Friday'), open('Saturday', '10:00', '14:00'),
    ]);
    expect(runs.map((run) => [run.first, run.last, run.isClosed, run.opens, run.closes])).toEqual([
      ['Sunday', 'Thursday', false, '09:00', '18:00'],
      ['Friday', 'Friday', true, null, null],
      ['Saturday', 'Saturday', false, '10:00', '14:00'],
    ]);
    expect(runs[0].days).toHaveLength(5);
  });

  it('never merges days that are not next to each other', () => {
    const runs = groupHours([open('Sunday'), shut('Monday'), open('Tuesday')]);
    expect(runs).toHaveLength(3);
  });

  it('reads seconds and no seconds as the same time', () => {
    expect(groupHours([open('Sunday', '09:00:00', '18:00:00'), open('Monday', '09:00', '18:00')])).toHaveLength(1);
  });

  it('treats a day missing either time as closed, as the office page always has', () => {
    const runs = groupHours([{ day: 'Sunday', isClosed: false, opens: '09:00', closes: null }, shut('Monday')]);
    expect(runs).toEqual([{ first: 'Sunday', last: 'Monday', days: ['Sunday', 'Monday'], isClosed: true, opens: null, closes: null }]);
  });

  it('is empty for an office that sent no week', () => {
    expect(groupHours([])).toEqual([]);
  });

  it('names today in the platform time zone, not the browser one', () => {
    // 2026-09-24 22:30 UTC is already Friday in Amman (UTC+3).
    const instant = new Date(Date.UTC(2026, 8, 24, 22, 30));
    expect(weekdayIn('Asia/Amman', instant)).toBe('Friday');
    expect(weekdayIn('UTC', instant)).toBe('Thursday');
  });
});
