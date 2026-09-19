import { TranslationKey } from './en';
import { MessageParams } from './language';

/** Passed in rather than injected, so presenters and their specs can call these directly. */
type Translate = (key: TranslationKey, params?: MessageParams) => string;

const MINUTE = 60_000;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;

/**
 * A moment relative to `now`, in either direction: "3 hr ago", "in 5 hr", "yesterday", "قبل 3 ساعات".
 *
 * ICU words it rather than the dictionary. `Intl.RelativeTimeFormat` already knows each locale's
 * plural forms and its own "yesterday" and "tomorrow", and the locale tag carries the console's
 * Latin-digit pin. The dictionary version this replaced only ever counted backwards — it computed
 * `now - then`, so an upcoming pickup came out as a negative number of minutes and was labelled
 * "Just now" in the notifications panel.
 */
export function relativeTime(
  value: string | number | Date,
  now: number,
  localeTag: string,
): string {
  const at =
    value instanceof Date ? value.getTime() : typeof value === 'number' ? value : Date.parse(value);
  if (!Number.isFinite(at)) return '—';

  const delta = at - now;
  const sign = delta < 0 ? -1 : 1;
  const format = new Intl.RelativeTimeFormat(localeTag, { numeric: 'auto', style: 'short' });

  const minutes = Math.round(Math.abs(delta) / MINUTE);
  // Under a minute either way is "now". Zero minutes would read "this minute", which nobody says.
  if (minutes < 1) return format.format(0, 'second');
  if (minutes < 60) return format.format(sign * minutes, 'minute');
  const hours = Math.round(Math.abs(delta) / HOUR);
  if (hours < 24) return format.format(sign * hours, 'hour');
  return format.format(sign * Math.round(Math.abs(delta) / DAY), 'day');
}

/**
 * A length of time as compact as the badge it sits in: "40m", "13h", "2d".
 *
 * A dictionary message rather than `Intl.NumberFormat` units. ICU's narrow Arabic units ("13 س",
 * "2 ي") are abbreviations nobody reads, so Arabic spells the unit out, in whichever of its plural
 * forms the count takes.
 */
export function durationPhrase(ms: number, t: Translate): string {
  const minutes = Math.round(Math.abs(ms) / MINUTE);
  if (minutes < 60) return t('time.minutesShort', { count: minutes });
  const hours = Math.round(minutes / 60);
  if (hours < 48) return t('time.hoursShort', { count: hours });
  return t('time.daysShort', { count: Math.round(hours / 24) });
}

/**
 * The whole units a clock would show for a stretch of time: "40m", "13h", "2d".
 *
 * The same flooring and the same one-minute floor as the readings below, so a badge that shows only
 * the figure cannot disagree with the line beside it — "SLA 0h" against "59m remaining" was exactly
 * that disagreement.
 */
export function clockDuration(ms: number, t: Translate): string {
  const { count, unit } = clockParts(ms);
  const keys: Record<ClockUnit, TranslationKey> = {
    minutes: 'time.minutesShort',
    hours: 'time.hoursShort',
    days: 'time.daysShort',
  };
  return t(keys[unit], { count });
}

/** What a clock against a deadline says, and whether that deadline has passed. */
export interface ClockReading {
  /** The words on the screen: "2h remaining", "Overdue by 13h", "Expired". Empty for an unreadable date. */
  readonly text: string;
  /** Whether the deadline has passed — by the server's word, or by this clock. */
  readonly passed: boolean;
}

type ClockUnit = 'minutes' | 'hours' | 'days';

const REMAINING: Record<ClockUnit, TranslationKey> = {
  minutes: 'time.remainingMinutes',
  hours: 'time.remainingHours',
  days: 'time.remainingDays',
};

const OVERDUE: Record<ClockUnit, TranslationKey> = {
  minutes: 'time.overdueMinutes',
  hours: 'time.overdueHours',
  days: 'time.overdueDays',
};

/**
 * A stretch of time in the one unit a person reads it in, in WHOLE units, floored.
 *
 * Floored, because a clock that rounds up promises time that is not there: 1h31m left is "1h
 * remaining", never "2h". Minutes under an hour, hours under two days, days after that — the units
 * the badges have always used. Never below one minute: "0m remaining" reads as already gone.
 */
function clockParts(ms: number): { count: number; unit: ClockUnit } {
  const minutes = Math.max(1, Math.floor(ms / MINUTE));
  if (minutes < 60) return { count: minutes, unit: 'minutes' };
  const hours = Math.floor(minutes / 60);
  if (hours < 48) return { count: hours, unit: 'hours' };
  return { count: Math.floor(hours / 24), unit: 'days' };
}

const instant = (deadline: string | number): number =>
  typeof deadline === 'number' ? deadline : Date.parse(deadline);

/**
 * A chance that runs out: a request the rental office must answer, a deposit waiting to be paid, a
 * signed link. While it runs, "2h remaining"; once it has passed, "Expired", which is terminal and
 * carries no amount — an opportunity that has gone is not late, it is over.
 *
 * `closed` is the server's own answer that the chance has LAPSED — the caller passes it only where
 * that is what the flag means, since `isAwaitingDecision === false` is also true of a booking that was
 * answered in time. It wins over this clock
 * in one direction only: a phone whose time is behind must not show a dead request as live. A clock
 * that is AHEAD of the server still reads the deadline as passed, because the moment itself is the
 * server's and only the comparison is local. Terminal = the flag OR the clock, never the clock alone.
 */
export function deadlineReading(
  deadline: string | number,
  now: number,
  t: Translate,
  closed = false,
): ClockReading {
  const due = instant(deadline);
  if (closed || (Number.isFinite(due) && due <= now))
    return { text: t('time.expired'), passed: true };
  if (!Number.isFinite(due)) return { text: '', passed: false };
  const { count, unit } = clockParts(due - now);
  return { text: t(REMAINING[unit], { count }), passed: false };
}

/**
 * A promise the platform made that can be broken: a dispute's SLA, a dealer application's review
 * window. While it runs, "2h remaining"; once broken, "Overdue by 13h" — by how much it was broken,
 * which is the number an administrator acts on (owner, 2026-09-17).
 *
 * `breached` is the server's answer (`isOverdue`, `isBreachingSla`). When the server says the
 * promise is broken but this clock still sees time left, the reading is plain "Overdue": the server
 * is right about the fact, and this clock cannot say by how much without inventing a figure.
 */
export function slaReading(
  deadline: string | number,
  now: number,
  t: Translate,
  breached = false,
): ClockReading {
  const due = instant(deadline);
  if (!Number.isFinite(due)) return { text: breached ? t('time.overdue') : '', passed: breached };
  if (due <= now) {
    const { count, unit } = clockParts(now - due);
    return { text: t(OVERDUE[unit], { count }), passed: true };
  }
  if (breached) return { text: t('time.overdue'), passed: true };
  const { count, unit } = clockParts(due - now);
  return { text: t(REMAINING[unit], { count }), passed: false };
}
