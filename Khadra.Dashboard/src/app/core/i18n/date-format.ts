/**
 * A calendar DATE the server sent as `YYYY-MM-DD` — a report's first day, a booking trend's bucket —
 * formatted without letting a time zone move it.
 *
 * A date is not an instant. `new Date('2026-09-06')` is midnight UTC, which is already the fifth of
 * September anywhere west of Greenwich, and `new Date('2026-09-06T00:00:00')` is local midnight,
 * which a formatter pinned to another zone can carry back a day. So the date is placed at noon UTC
 * and formatted in UTC: no zone on Earth is twelve hours from it.
 */
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
