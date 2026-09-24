import { GalleryDaySchedule } from '../../core/api/catalogue.api';

/** A run of consecutive days, in the order the office's week arrives, that keep the same hours. */
export interface HoursRun {
  /** The API's English day names, first and last of the run (the same day for a run of one). */
  readonly first: string;
  readonly last: string;
  readonly days: readonly string[];
  readonly isClosed: boolean;
  readonly opens: string | null;
  readonly closes: string | null;
}

/** A day with no complete pair of times is closed, exactly as the office's page has always shown it. */
function closed(day: GalleryDaySchedule): boolean {
  return day.isClosed || !day.opens || !day.closes;
}

/** `09:00:00` and `09:00` are the same opening time. */
function minute(value: string | null): string | null {
  return value ? value.slice(0, 5) : null;
}

/**
 * Seven lines become two or three: "Sunday – Thursday 09:00 – 18:00, Friday closed". Only NEIGHBOURING
 * days merge, in the week's own order, so the reader never has to work out which days a range skips.
 */
export function groupHours(days: readonly GalleryDaySchedule[]): HoursRun[] {
  const runs: { first: string; last: string; days: string[]; isClosed: boolean; opens: string | null; closes: string | null }[] = [];
  for (const day of days) {
    const isClosed = closed(day);
    const opens = isClosed ? null : minute(day.opens);
    const closes = isClosed ? null : minute(day.closes);
    const previous = runs.at(-1);
    if (previous && previous.isClosed === isClosed && previous.opens === opens && previous.closes === closes) {
      previous.last = day.day;
      previous.days.push(day.day);
    } else {
      runs.push({ first: day.day, last: day.day, days: [day.day], isClosed, opens, closes });
    }
  }
  return runs;
}

/** The English weekday of an instant in the platform's time zone: the key the API's days use. */
export function weekdayIn(timeZone: string, now: Date): string {
  return new Intl.DateTimeFormat('en-US', { weekday: 'long', timeZone }).format(now);
}
