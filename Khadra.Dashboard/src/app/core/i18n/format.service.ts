import { Injectable, computed, inject } from '@angular/core';
import { I18nService } from './i18n.service';

const FSI = '\u2068';
const PDI = '\u2069';

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
      new Intl.DateTimeFormat(this.locale(), { weekday: 'long', timeZone: 'UTC' }).format(reference),
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
    const date = this.parse(value);
    return date
      ? this.isolate(
          new Intl.DateTimeFormat(this.locale(), {
            day: '2-digit',
            month: 'short',
            year: 'numeric',
            calendar: 'gregory',
            timeZone: this.timeZone,
          }).format(date),
        )
      : '—';
  }

  /** 06 Sept 2026, 14:32 */
  dateTime(value: string | number | Date | null | undefined): string {
    const date = this.parse(value);
    return date
      ? this.isolate(
          new Intl.DateTimeFormat(this.locale(), {
            day: '2-digit',
            month: 'short',
            year: 'numeric',
            hour: '2-digit',
            minute: '2-digit',
            hourCycle: 'h23',
            calendar: 'gregory',
            timeZone: this.timeZone,
          }).format(date),
        )
      : '—';
  }

  /** 14:32 */
  time(value: string | number | Date | null | undefined): string {
    const date = this.parse(value);
    return date
      ? this.isolate(
          new Intl.DateTimeFormat(this.locale(), {
            hour: '2-digit',
            minute: '2-digit',
            hourCycle: 'h23',
            timeZone: this.timeZone,
          }).format(date),
        )
      : '—';
  }

  number(value: number | null | undefined, fractionDigits?: number): string {
    if (value === null || value === undefined || !Number.isFinite(value)) return '—';
    return this.isolate(
      new Intl.NumberFormat(this.locale(), {
        minimumFractionDigits: fractionDigits,
        maximumFractionDigits: fractionDigits ?? 3,
      }).format(value),
    );
  }

  percent(value: number | null | undefined): string {
    if (value === null || value === undefined || !Number.isFinite(value)) return '—';
    return this.isolate(`${new Intl.NumberFormat(this.locale()).format(value)}%`);
  }

  /**
   * "35 JOD", with the code from the value itself.
   *
   * The code is never defaulted: a hard-coded 'JOD' would be this console asserting something about
   * a number that came from somewhere else. Amount and code travel together as one isolated run so
   * the pair does not get split by the surrounding Arabic.
   */
  money(amount: number | null | undefined, currency: string | null | undefined): string {
    if (amount === null || amount === undefined || !Number.isFinite(amount)) return '—';
    // Padded to the currency's own scale, so a 110.000 JOD booking does not print as "110"
    // here while the customer app shows "JOD 110.000" for the same figure at the same moment.
    // The dinar is divided into a thousand fils; the scale comes from the server, not a guess.
    const minorUnits = this.currencyMinorUnits;
    const formatted = new Intl.NumberFormat(this.locale(), {
      minimumFractionDigits: minorUnits ?? 0,
      maximumFractionDigits: minorUnits ?? 3,
    }).format(amount);
    return this.isolate(currency ? `${formatted} ${currency}` : formatted);
  }

  private parse(value: string | number | Date | null | undefined): Date | null {
    if (value === null || value === undefined || value === '') return null;
    const date = value instanceof Date ? value : new Date(value);
    return Number.isNaN(date.getTime()) ? null : date;
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

