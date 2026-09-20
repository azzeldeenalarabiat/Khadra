import { Injectable, computed, inject } from '@angular/core';
import { I18nService } from './i18n.service';
import { formatAmount, formatNumber, formatPercent, formatPercentRange } from './number-format';
import {
  ClockReading,
  deadlineReading,
  durationPhrase,
  relativeTime,
  slaReading,
} from './relative-time';

const FSI = '⁨';
const PDI = '⁩';
const DAY_MS = 86_400_000;

/**
 * Every number, date and amount the console prints, in the language it is currently speaking.
 *
 * It exists because `toLocaleString('en-GB')` was written at two dozen call sites. Those are correct
 * only while the console is English: under Arabic they print English month names beside Arabic
 * chrome, and there is no single place to change it. Everything goes through `Intl` here instead,
 * with the locale taken from the switcher.
 *
 * ## Digits are Latin, deliberately
 *
 * `ar-JO` defaults to Arabic-Indic digits (٠١٢٣), so an unpinned `Intl` would print `١٢٣` for a
 * price while the plate number, the booking reference and the phone number beside it — all sent by
 * the server as text — stayed `123`. Jordanian commercial software, bank statements and invoices use
 * Latin digits, and the Flutter app will too. `-u-nu-latn` on the locale tag pins it. It is one
 * constant to change if the owner ever wants otherwise, which is the reason for routing every site
 * through here rather than leaving the calls scattered.
 */
@Injectable({ providedIn: 'root' })
export class FormatService {
  private readonly i18n = inject(I18nService);

  /**
   * The zone dates are shown in.
   *
   * The browser's, as it has always been. The platform's own reporting zone is a server setting
   * (`AdminDashboard:ReportingTimeZone`), and the console is not told it except on the audit screen,
   * so writing `Asia/Amman` here would be this console inventing a fact. Left as a seam: when the
   * server exposes it, set it once and every date in the console follows.
   */
  private timeZone: string | undefined = undefined;

  /**
   * How many decimal places this platform's currency is held at, once the server has said.
   *
   * Undefined until then, and undefined means "print it at whatever scale it arrived at" -- the
   * behaviour the console had before it asked. Never defaulted to 3: a hard-coded scale would be
   * this console asserting something about a currency it was not told about.
   */
  private currencyMinorUnits: number | undefined = undefined;

  /** Set once, from `/api/v1/app-config`. See PlatformConfigService. */
  useCurrencyMinorUnits(units: number | undefined): void {
    this.currencyMinorUnits =
      typeof units === 'number' && Number.isInteger(units) && units >= 0 && units <= 4
        ? units
        : undefined;
  }

  /** Set once, when the server tells the console which zone its reporting day runs on. */
  useTimeZone(zone: string | undefined): void {
    this.timeZone = zone;
  }

  private readonly locale = computed(() => this.i18n.localeTag());

  /**
   * A weekday name in the reader's language.
   *
   * The server sends operating hours keyed by the English day name ('Sunday' … 'Saturday'), which
   * left an Arabic, right-to-left opening-hours table reading "Sunday". Rather than hand-translate
   * seven words, the name is turned back into a position in the week and handed to ICU, which
   * already knows what every locale calls its days. 2024-01-07 is a Sunday, so it anchors the week.
   * An unrecognised value is returned untouched instead of guessed at.
   */
  weekday(englishName: string | null | undefined): string {
    if (!englishName) return '';
    const index = FormatService.WeekdayOrder.indexOf(englishName.trim().toLowerCase());
    if (index < 0) return englishName;
    const reference = new Date(Date.UTC(2024, 0, 7 + index));
    return this.isolate(
      new Intl.DateTimeFormat(this.locale(), { weekday: 'long', timeZone: 'UTC' }).format(
        reference,
      ),
    );
  }

  private static readonly WeekdayOrder = [
    'sunday',
    'monday',
    'tuesday',
    'wednesday',
    'thursday',
    'friday',
    'saturday',
  ];

  /** 06 Sept 2026 */
  date(value: string | number | Date | null | undefined): string {
    return this.format(value, {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
      calendar: 'gregory',
    });
  }

  /** 06 Sept — for a list or a caption where the year would be noise. */
  dayMonth(value: string | number | Date | null | undefined): string {
    return this.format(value, { day: '2-digit', month: 'short', calendar: 'gregory' });
  }

  /** 06 Sept, 14:32 */
  dayMonthTime(value: string | number | Date | null | undefined): string {
    return this.format(value, {
      day: '2-digit',
      month: 'short',
      hour: '2-digit',
      minute: '2-digit',
      hourCycle: 'h23',
      calendar: 'gregory',
    });
  }

  /** September 2026 */
  monthYear(value: string | number | Date | null | undefined): string {
    return this.format(value, { month: 'long', year: 'numeric', calendar: 'gregory' });
  }

  /** 06 Sept 2026, 14:32 */
  dateTime(value: string | number | Date | null | undefined): string {
    return this.format(value, {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      hourCycle: 'h23',
      calendar: 'gregory',
    });
  }

  /**
   * 06 Sept 2026, 14:32:09 — an append-only record where two entries in the same minute are common.
   *
   * Seconds are shown for one reason: the audit log is strictly ordered, and without them a sequence
   * of decisions reads as simultaneous.
   */
  dateTimeSeconds(value: string | number | Date | null | undefined): string {
    return this.format(value, {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hourCycle: 'h23',
      calendar: 'gregory',
    });
  }

  /** 14:32 */
  time(value: string | number | Date | null | undefined): string {
    return this.format(value, { hour: '2-digit', minute: '2-digit', hourCycle: 'h23' });
  }

  /**
   * 06 Sept, for a calendar DATE the server sent as `YYYY-MM-DD` — a report's first day, a booking
   * trend's bucket.
   *
   * A date is not an instant, so it must not pass through a time zone: `new Date('2026-09-06')` is
   * midnight UTC, which is the fifth of September anywhere west of Greenwich. It is formatted at noon
   * UTC, in UTC, so no zone can move it to a neighbouring day.
   */
  calendarDayMonth(isoDate: string | null | undefined): string {
    const date = this.calendarDate(isoDate);
    return date
      ? this.isolate(
          new Intl.DateTimeFormat(this.locale(), {
            day: '2-digit',
            month: 'short',
            calendar: 'gregory',
            timeZone: 'UTC',
          }).format(date),
        )
      : '—';
  }

  /** Tue 2 Sept, for a calendar date. The same zone rule as `calendarDayMonth`. */
  calendarWeekdayDayMonth(isoDate: string | null | undefined): string {
    const date = this.calendarDate(isoDate);
    return date
      ? this.isolate(
          new Intl.DateTimeFormat(this.locale(), {
            weekday: 'short',
            day: 'numeric',
            month: 'short',
            calendar: 'gregory',
            timeZone: 'UTC',
          }).format(date),
        )
      : '—';
  }

  /**
   * "today 14:32", "tomorrow 09:00", "06 Sept 14:32" — when a handover is, for someone planning a day.
   *
   * Today, tomorrow and yesterday are ICU's own words (`numeric: 'auto'`), counted in calendar days in
   * the zone dates are shown in, not in 24-hour steps: 23:00 tonight and 01:00 are different days.
   */
  dayAndTime(value: string | number | Date | null | undefined, now: number = Date.now()): string {
    const date = this.parse(value);
    if (!date) return '—';
    const days = this.dayNumber(date) - this.dayNumber(new Date(now));
    const time = this.time(date);
    if (Math.abs(days) <= 1) {
      const day = new Intl.RelativeTimeFormat(this.locale(), { numeric: 'auto' }).format(
        days,
        'day',
      );
      return `${day} ${time}`;
    }
    return `${this.dayMonth(date)} ${time}`;
  }

  /** "3 hr ago", "in 2 days", "yesterday" — see `relativeTime`. */
  relative(value: string | number | Date | null | undefined, now: number = Date.now()): string {
    if (value === null || value === undefined || value === '') return '—';
    return relativeTime(value, now, this.locale());
  }

  /** "40m", "13h", "2d" — see `durationPhrase`. */
  duration(ms: number): string {
    return durationPhrase(ms, this.i18n.t);
  }

  /**
   * A chance that runs out — a request to answer, a deposit to pay, a signed link: "2h remaining",
   * then "Expired". `closed` is the server's own answer, and wins. See `deadlineReading`.
   */
  deadline(deadline: string | number, closed = false, now: number = Date.now()): ClockReading {
    return deadlineReading(deadline, now, this.i18n.t, closed);
  }

  /**
   * A promise that can be broken — a dispute SLA, an application review: "2h remaining", then
   * "Overdue by 13h". `breached` is the server's own answer, and wins. See `slaReading`.
   */
  sla(deadline: string | number, breached = false, now: number = Date.now()): ClockReading {
    return slaReading(deadline, now, this.i18n.t, breached);
  }

  number(value: number | null | undefined, fractionDigits?: number): string {
    if (value === null || value === undefined || !Number.isFinite(value)) return '—';
    return this.isolate(formatNumber(value, this.locale(), fractionDigits));
  }

  percent(value: number | null | undefined): string {
    if (value === null || value === undefined || !Number.isFinite(value)) return '—';
    return this.isolate(formatPercent(value, this.locale()));
  }

  /**
   * "25–50%" — a range of percentages as ONE isolated run, for the reason `moneyRange` is: two
   * isolates around a dash are laid out right to left under Arabic, upper bound first. Equal bounds
   * are just the percentage.
   */
  percentRange(min: number | null | undefined, max: number | null | undefined): string {
    if (
      min === null ||
      min === undefined ||
      max === null ||
      max === undefined ||
      !Number.isFinite(min) ||
      !Number.isFinite(max)
    ) {
      return '—';
    }
    return this.isolate(formatPercentRange(min, max, this.locale()));
  }

  /**
   * "31.9539, 35.9106" — a map point as ONE left-to-right run.
   *
   * Two numbers joined by ", " inside an Arabic paragraph are laid out right to left, so the longitude
   * lands where the latitude should be. Isolated together, the pair reads in the order it is written.
   */
  coordinates(latitude: number | null | undefined, longitude: number | null | undefined): string {
    if (
      latitude === null ||
      latitude === undefined ||
      longitude === null ||
      longitude === undefined ||
      !Number.isFinite(latitude) ||
      !Number.isFinite(longitude)
    ) {
      return '—';
    }
    const locale = this.locale();
    return this.isolate(
      `${formatNumber(latitude, locale, 4)}, ${formatNumber(longitude, locale, 4)}`,
    );
  }

  /**
   * "35.000 JOD", with the code from the value itself.
   *
   * The code is never defaulted: a hard-coded 'JOD' would be this console asserting something about
   * a number that came from somewhere else. Amount and code travel together as one isolated run so
   * the pair does not get split by the surrounding Arabic.
   *
   * Padded to the currency's own scale, so a 110.000 JOD booking does not print as "110" here while
   * the customer app shows "JOD 110.000" for the same figure at the same moment. The dinar is divided
   * into a thousand fils; the scale comes from the server, not a guess. And never "−0": see
   * `withoutNegativeZero`.
   */
  money(amount: number | null | undefined, currency: string | null | undefined): string {
    if (amount === null || amount === undefined || !Number.isFinite(amount)) return '—';
    const formatted = formatAmount(amount, this.locale(), this.currencyMinorUnits);
    return this.isolate(currency ? `${formatted} ${currency}` : formatted);
  }

  /**
   * "20.000–35.000 JOD" — a range as ONE isolated run.
   *
   * Two `money()` results joined by a dash are two isolates with a neutral between them, and Arabic
   * lays them out right to left: the upper bound first. A single run keeps the range reading the way
   * it was written. Equal bounds are just the amount.
   */
  moneyRange(
    min: number | null | undefined,
    max: number | null | undefined,
    currency: string | null | undefined,
  ): string {
    if (min === null || min === undefined || !Number.isFinite(min))
      return this.money(max, currency);
    if (max === null || max === undefined || !Number.isFinite(max) || max === min) {
      return this.money(min, currency);
    }
    const low = formatAmount(min, this.locale(), this.currencyMinorUnits);
    const high = formatAmount(max, this.locale(), this.currencyMinorUnits);
    return this.isolate(currency ? `${low}–${high} ${currency}` : `${low}–${high}`);
  }

  private format(
    value: string | number | Date | null | undefined,
    options: Intl.DateTimeFormatOptions,
  ): string {
    const date = this.parse(value);
    return date
      ? this.isolate(
          new Intl.DateTimeFormat(this.locale(), { ...options, timeZone: this.timeZone }).format(
            date,
          ),
        )
      : '—';
  }

  private parse(value: string | number | Date | null | undefined): Date | null {
    if (value === null || value === undefined || value === '') return null;
    const date = value instanceof Date ? value : new Date(value);
    return Number.isNaN(date.getTime()) ? null : date;
  }

  /** `YYYY-MM-DD` as noon UTC on that day, or null when it is not a date. */
  private calendarDate(isoDate: string | null | undefined): Date | null {
    const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(isoDate ?? '');
    return match
      ? new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3]), 12))
      : null;
  }

  /** Whole days since the epoch, counted in the zone dates are shown in. */
  private dayNumber(date: Date): number {
    // en-CA prints ISO order (2026-09-06). A machine key for arithmetic, never shown to anyone.
    const [year, month, day] = new Intl.DateTimeFormat('en-CA', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      timeZone: this.timeZone,
    })
      .format(date)
      .split('-')
      .map(Number);
    return Math.round(Date.UTC(year, month - 1, day) / DAY_MS);
  }

  /**
   * Wraps a run of Latin digits and letters so Arabic around it does not reorder it.
   *
   * "−30 JOD" renders as "JOD 30−" inside an RTL paragraph without this, which is a different
   * number to the one the server sent.
   */
  private isolate(text: string): string {
    return this.i18n.isRtl() ? `${FSI}${text}${PDI}` : text;
  }
}
