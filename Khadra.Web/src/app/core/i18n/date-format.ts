/**
 * A calendar DATE the server sent as `YYYY-MM-DD` — a report's first day, a booking trend's bucket —
 * formatted without letting a time zone move it.
 *
 * A date is not an instant. `new Date('2026-09-06')` is midnight UTC, which is already the fifth of
 * September anywhere west of Greenwich, and `new Date('2026-09-06T00:00:00')` is local midnight,
 * which a formatter pinned to another zone can carry back a day. So the date is placed at noon UTC
 * and formatted in UTC: no zone on Earth is twelve hours from it.
 */
/**
 * A wall time a DOCUMENT froze — `"2026-09-27 11:17"`, already Amman's (payments Phase 5b) — printed as
 * that same wall time.
 *
 * Moving it through any zone would print a different hour on a machine set elsewhere, and re-deriving it
 * from the document's UTC instant would re-judge a record by today's zone rules. So, as a calendar date
 * is, it is placed in UTC and formatted in UTC. Answers null for text that is not such a time, which
 * the caller prints as it is.
 */
export function formatFrozenLocal(
  local: string | null | undefined,
  localeTag: string,
  options: Intl.DateTimeFormatOptions,
): string | null {
  const match = /^(\d{4})-(\d{2})-(\d{2}) (\d{2}):(\d{2})$/.exec(local ?? '');
  if (!match) return null;
  const [year, month, day, hour, minute] = match.slice(1).map(Number) as [number, number, number, number, number];
  const wall = new Date(Date.UTC(year, month - 1, day, hour, minute));
  // Date.UTC rolls "2026-13-45 99:99" over into another date; a record prints as stored instead.
  if (
    wall.getUTCFullYear() !== year ||
    wall.getUTCMonth() !== month - 1 ||
    wall.getUTCDate() !== day ||
    wall.getUTCHours() !== hour ||
    wall.getUTCMinutes() !== minute
  )
    return null;
  return new Intl.DateTimeFormat(localeTag, { ...options, calendar: 'gregory', timeZone: 'UTC' }).format(wall);
}

export function formatCalendarDate(
  isoDate: string | null | undefined,
  localeTag: string,
  options: Intl.DateTimeFormatOptions,
): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(isoDate ?? '');
  if (!match) return '—';
  const noon = new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3]), 12));
  return new Intl.DateTimeFormat(localeTag, {
    ...options,
    calendar: 'gregory',
    timeZone: 'UTC',
  }).format(noon);
}
