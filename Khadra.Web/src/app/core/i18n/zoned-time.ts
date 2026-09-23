/**
 * Wall-clock times in the PLATFORM's zone (from `/app-config`), converted to and from instants.
 *
 * A customer picking "25 Sept, 10:00" means 10:00 at the rental office, wherever their browser is. The
 * URL keeps that wall-clock value (readable and shareable); the API is sent the instant it names. No
 * library: `Intl` knows every zone's offsets, and the conversion below asks it rather than assuming a
 * fixed one, so a zone that ever changes its rules is still right.
 */

export interface WallClock {
  /** YYYY-MM-DD */
  readonly date: string;
  /** HH:mm */
  readonly time: string;
}

const DATE = /^(\d{4})-(\d{2})-(\d{2})$/;
const TIME = /^([01]\d|2[0-3]):([0-5]\d)$/;

/** The instant a wall-clock date and time in `timeZone` names, or null if either is malformed. */
export function wallClockToInstant(value: WallClock, timeZone: string): Date | null {
  const d = DATE.exec(value.date);
  const t = TIME.exec(value.time);
  if (!d || !t) return null;
  const asUtc = Date.UTC(Number(d[1]), Number(d[2]) - 1, Number(d[3]), Number(t[1]), Number(t[2]));
  if (Number.isNaN(asUtc)) return null;

  // Guess with the offset at the naive instant, then correct once with the offset at the guess. Two
  // passes settle every zone that moves by whole offsets, including across a daylight-saving change.
  let instant = asUtc - offsetMs(new Date(asUtc), timeZone);
  instant = asUtc - offsetMs(new Date(instant), timeZone);
  return new Date(instant);
}

/** The wall-clock date and time an instant shows in `timeZone`. */
export function instantToWallClock(instant: Date, timeZone: string): WallClock {
  const parts = partsIn(instant, timeZone);
  return {
    date: `${parts.year}-${pad(parts.month)}-${pad(parts.day)}`,
    time: `${pad(parts.hour)}:${pad(parts.minute)}`,
  };
}

/** YYYY-MM-DD plus `days`, as a calendar date (no zone involved). */
export function addDays(date: string, days: number): string {
  const d = DATE.exec(date);
  if (!d) return date;
  const moved = new Date(Date.UTC(Number(d[1]), Number(d[2]) - 1, Number(d[3]) + days));
  return `${moved.getUTCFullYear()}-${pad(moved.getUTCMonth() + 1)}-${pad(moved.getUTCDate())}`;
}

export function isWallClock(value: Partial<WallClock> | null | undefined): value is WallClock {
  return !!value && DATE.test(value.date ?? '') && TIME.test(value.time ?? '');
}

function offsetMs(instant: Date, timeZone: string): number {
  const p = partsIn(instant, timeZone);
  const asUtc = Date.UTC(p.year, p.month - 1, p.day, p.hour, p.minute, p.second);
  return asUtc - Math.floor(instant.getTime() / 1000) * 1000;
}

function partsIn(instant: Date, timeZone: string) {
  const formatted = new Intl.DateTimeFormat('en-US', {
    timeZone,
    hourCycle: 'h23',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  }).formatToParts(instant);
  const get = (type: Intl.DateTimeFormatPartTypes) => Number(formatted.find((part) => part.type === type)?.value);
  return { year: get('year'), month: get('month'), day: get('day'), hour: get('hour') % 24, minute: get('minute'), second: get('second') };
}

function pad(value: number): string {
  return String(value).padStart(2, '0');
}
