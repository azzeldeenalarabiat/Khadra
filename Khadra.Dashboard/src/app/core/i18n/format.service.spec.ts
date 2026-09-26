import '@angular/compiler';
import { Injector, runInInjectionContext } from '@angular/core';
import { describe, expect, it } from 'vitest';
import { FormatService } from './format.service';
import { I18nService } from './i18n.service';

/**
 * A month the server sent as a calendar date is a month, not an instant (payments Phase 4b).
 *
 * The dashboard's money panel names the Amman month its figures cover, and the server sends that
 * month's first day as `YYYY-MM-DD`. Read as an instant, `2026-09-01` is midnight UTC, which is the
 * evening of 31 August in any browser west of Greenwich, so the September panel was headed "August
 * 2026". The display zone is the formatter's own seam (`useTimeZone`), so a western browser is
 * reproduced here without depending on the machine the tests run on.
 */
function formatterIn(localeTag: string, zone: string): FormatService {
  const injector = Injector.create({
    providers: [
      {
        provide: I18nService,
        useValue: { localeTag: () => localeTag, isRtl: () => localeTag.startsWith('ar') },
      },
    ],
  });
  const formats = runInInjectionContext(injector, () => new FormatService());
  formats.useTimeZone(zone);
  return formats;
}

const visible = (text: string) => text.replace(/[\u2068\u2069]/g, '');

describe('calendarMonthYear', () => {
  it('names the month of a calendar date in a browser west of Greenwich, where an instant slips a day back', () => {
    const formats = formatterIn('en-GB', 'America/New_York');

    expect(visible(formats.monthYear('2026-09-01'))).toBe('August 2026');
    expect(visible(formats.calendarMonthYear('2026-09-01'))).toBe('September 2026');
  });

  // ar-JO names the months the Levantine way, as everything else in the Arabic console does.
  it('names it in Arabic too, and says nothing about a value that is not a date', () => {
    const formats = formatterIn('ar-JO-u-nu-latn', 'Pacific/Honolulu');

    expect(visible(formats.calendarMonthYear('2026-09-01'))).toBe('أيلول 2026');
    expect(formats.calendarMonthYear(null)).toBe('—');
    expect(formats.calendarMonthYear('September')).toBe('—');
  });
});
