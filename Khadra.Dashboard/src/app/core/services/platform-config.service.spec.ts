import { HttpClient } from '@angular/common/http';
import { Injector, runInInjectionContext } from '@angular/core';
import { Observable, of, throwError } from 'rxjs';
import { describe, expect, it } from 'vitest';
import { formatAmount } from '../i18n/number-format';
import { FormatService } from '../i18n/format.service';
import { PlatformConfigService } from './platform-config.service';

/**
 * The platform's own facts, read before the console prints a single figure.
 *
 * The console does not guess the currency's scale: a hard-coded 3 would be this console asserting
 * something about a currency nobody told it about. So it asks — and until this was fixed, the asking
 * failed. `GET /api/v1/app-config` is anonymous on the API, but the BFF forwarded nothing under
 * `/api` without a session, so a console opened at the sign-in page (every normal sign-in) got a 401,
 * swallowed it by design, and ran the whole session on fallbacks. Signing in did not repair it:
 * sign-in navigates inside the application, and the configuration is asked for once, at startup.
 *
 * What that looked like is the last test here: a 110.000 JOD booking printed as "110".
 */
const AS_THE_API_ANSWERS = {
  timeZone: 'Asia/Amman',
  currency: { code: 'JOD', minorUnits: 3 },
};

/** Records what the service pushed into the formatter. */
class Recorder {
  minorUnits: number | undefined = undefined;
  timeZone: string | undefined = undefined;
  calls = 0;

  useCurrencyMinorUnits(units: number | undefined): void {
    this.calls++;
    this.minorUnits = units;
  }

  useTimeZone(zone: string | undefined): void {
    this.timeZone = zone;
  }
}

/** The one call the service makes. `HttpClient.get` has too many overloads to stub wholesale. */
interface OneGet {
  get(url: string): Observable<unknown>;
}

function serviceWith(http: OneGet): {
  service: PlatformConfigService;
  formats: Recorder;
} {
  const formats = new Recorder();
  const injector = Injector.create({
    providers: [
      { provide: HttpClient, useValue: http },
      { provide: FormatService, useValue: formats },
    ],
  });
  return {
    service: runInInjectionContext(injector, () => new PlatformConfigService()),
    formats,
  };
}

describe('the platform configuration', () => {
  it('is read from the endpoint the customer app reads', async () => {
    const asked: string[] = [];
    const { service } = serviceWith({
      get: (url: string) => {
        asked.push(url);
        return of(AS_THE_API_ANSWERS);
      },
    });

    await service.load();

    expect(asked).toEqual(['/api/v1/app-config']);
  });

  it('gives the formatter the real currency scale and reporting zone', async () => {
    const { service, formats } = serviceWith({
      get: () => of(AS_THE_API_ANSWERS),
    });

    await service.load();

    expect(formats.minorUnits).toBe(3);
    expect(formats.timeZone).toBe('Asia/Amman');
  });

  it('does not hold the console up when the platform cannot be reached', async () => {
    // The failure is swallowed on purpose — a formatting nicety must not blank the screen. That is
    // exactly why the 401 was silent, and why the route had to be fixed rather than the swallow.
    const { service, formats } = serviceWith({
      get: () => throwError(() => ({ status: 401 })),
    });

    await expect(service.load()).resolves.toBeUndefined();
    expect(formats.calls).toBe(0);
  });

  it('is the difference between a dinar and a number that looks like one', () => {
    // Left unasked, `formatAmount` prints at whatever scale the figure arrived in.
    expect(formatAmount(110, 'en-GB', undefined)).toBe('110');
    expect(formatAmount(9.5, 'en-GB', undefined)).toBe('9.5');

    // Asked and answered: the dinar's thousand fils, as the customer app shows the same booking.
    expect(formatAmount(110, 'en-GB', 3)).toBe('110.000');
    expect(formatAmount(9.5, 'en-GB', 3)).toBe('9.500');
  });
});
