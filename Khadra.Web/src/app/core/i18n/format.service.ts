import { Injectable, computed, inject } from '@angular/core';
import { AppConfigService } from '../config/app-config.service';
import { Money } from '../api/common.api';
import { I18nService } from './i18n.service';
import { formatCalendarDate } from './date-format';
import { formatAmount, formatNumber } from './number-format';

const FSI = '⁨';
const PDI = '⁩';

/**
 * Every number, date and amount the website prints.
 *
 * Dates and times are shown in the PLATFORM's zone from `/app-config` (Asia/Amman today), never the
 * visitor's: a pickup at 10:00 is 10:00 at the rental office whoever is looking, and a page rendered
 * on the server in UTC must print the same hour the browser would. Until the configuration has
 * arrived there is no zone to print in, and a date prints as a dash rather than in a guessed one.
 *
 * Money always carries its own currency code, from the value itself — never a symbol typed into a
 * template — at the scale the platform publishes for that currency.
 */
@Injectable({ providedIn: 'root' })
export class FormatService {
  private readonly i18n = inject(I18nService);
  private readonly appConfig = inject(AppConfigService);

  private readonly locale = computed(() => this.i18n.localeTag());
  readonly timeZone = computed(() => this.appConfig.config()?.timeZone ?? null);

  money(value: Money | null | undefined): string {
    if (!value) return '—';
    const config = this.appConfig.config();
    const minorUnits = config && config.currency.code === value.currency ? config.currency.minorUnits : undefined;
    const digits = formatAmount(value.amount, this.locale(), minorUnits);
    return this.isolate(this.i18n.isArabic() ? `${digits} ${value.currency}` : `${value.currency} ${digits}`);
  }

  number(value: number, fractionDigits?: number): string {
    return formatNumber(value, this.locale(), fractionDigits);
  }

  /** 23 Sept 2026 */
  date(value: string | Date | null | undefined): string {
    return this.instant(value, { day: 'numeric', month: 'short', year: 'numeric' });
  }

  /** Wed, 23 Sept, 10:00 */
  dateTime(value: string | Date | null | undefined): string {
    return this.instant(value, {
      weekday: 'short',
      day: 'numeric',
      month: 'short',
      hour: '2-digit',
      minute: '2-digit',
      hourCycle: 'h23',
    });
  }

  /** 10:00 */
  time(value: string | Date | null | undefined): string {
    return this.instant(value, { hour: '2-digit', minute: '2-digit', hourCycle: 'h23' });
  }

  /** A `YYYY-MM-DD` the server sent as a date, not an instant: no zone can move it. */
  calendarDate(isoDate: string | null | undefined): string {
    return formatCalendarDate(isoDate, this.locale(), { day: 'numeric', month: 'short', year: 'numeric' });
  }

  /** A `HH:mm[:ss]` clock time from the server (opening hours), in the reader's digits and order. */
  clock(value: string | null | undefined): string {
    const match = /^(\d{2}):(\d{2})/.exec(value ?? '');
    if (!match) return '—';
    return this.isolate(`${match[1]}:${match[2]}`);
  }

  /** The day's name from the English name the API keys opening hours by. */
  weekday(englishName: string): string {
    const index = WEEKDAYS.indexOf(englishName.trim().toLowerCase());
    if (index < 0) return englishName;
    return new Intl.DateTimeFormat(this.locale(), { weekday: 'long', timeZone: 'UTC' }).format(
      new Date(Date.UTC(2024, 0, 7 + index)),
    );
  }

  private instant(value: string | Date | null | undefined, options: Intl.DateTimeFormatOptions): string {
    const zone = this.timeZone();
    if (!value || !zone) return '—';
    const date = value instanceof Date ? value : new Date(value);
    if (Number.isNaN(date.getTime())) return '—';
    return new Intl.DateTimeFormat(this.locale(), { ...options, calendar: 'gregory', timeZone: zone }).format(date);
  }

  private isolate(text: string): string {
    return this.i18n.isArabic() ? `${FSI}${text}${PDI}` : text;
  }
}

const WEEKDAYS = ['sunday', 'monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday'];
