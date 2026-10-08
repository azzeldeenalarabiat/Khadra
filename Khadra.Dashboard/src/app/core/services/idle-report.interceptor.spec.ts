import { HttpRequest, HttpResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { firstValueFrom, of } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { IDLE_REPORT_HEADER, idleReportInterceptor } from './idle-report.interceptor';

/**
 * Pre-launch item 129: every call the console makes says how long its person has been idle, so the
 * BFF can end an abandoned session however often the screen polls.
 */
describe('idleReportInterceptor', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.spyOn(performance, 'now').mockImplementation(() => Date.now());
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
  });

  afterEach(() => {
    TestBed.resetTestingModule();
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  async function headerOn(url: string): Promise<string | null> {
    let seen: string | null = null;
    await firstValueFrom(
      TestBed.runInInjectionContext(() =>
        idleReportInterceptor(new HttpRequest('GET', url), (request) => {
          seen = request.headers.get(IDLE_REPORT_HEADER);
          return of(new HttpResponse({ status: 200 }));
        }),
      ),
    );
    return seen;
  }

  it('reports the seconds since the last input on the console’s own calls', async () => {
    expect(await headerOn('/api/v1/admin/dashboard')).toBe('0');

    vi.advanceTimersByTime(12 * 60_000);
    expect(await headerOn('/bff/user')).toBe('720');

    document.dispatchEvent(new Event('keydown'));
    expect(await headerOn('/api/v1/bookings')).toBe('0');
  });

  it('says nothing to anybody else', async () => {
    expect(await headerOn('https://tiles.example.org/1/2/3.png')).toBeNull();
    expect(await headerOn('/assets/i18n.json')).toBeNull();
  });
});
