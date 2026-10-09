import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { HandoverCodeComponent, qrLibrary } from './handover-code.component';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';

const BOOKING = '01a0d082-8706-7748-af32-79cb5883e591';
const URL = `/api/v1/bookings/${BOOKING}/handover-code`;

/**
 * `qrcode` exactly as the PRODUCTION bundle hands it over: a CommonJS package, its exports under `default`
 * and nothing on the module itself (E2E F29). Under Node the package resolves to its server build, where
 * the real import works either way, so without this the tests could not see what broke in the browser.
 */
const qrDraw = vi.hoisted(() => ({
  draw: async (payload: string): Promise<string> => `data:image/png;base64,${btoa(payload)}`,
}));
vi.mock('qrcode', () => ({ default: { toDataURL: (payload: string) => qrDraw.draw(payload) } }));

describe('HandoverCodeComponent', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HandoverCodeComponent],
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  async function open(expected: 'Pickup' | 'Return') {
    const fixture = TestBed.createComponent(HandoverCodeComponent);
    fixture.componentRef.setInput('bookingId', BOOKING);
    fixture.componentRef.setInput('expected', expected);
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture;
  }

  /** Lets the awaited request (and the QR import after it) finish, then redraws. */
  async function settle(fixture: { detectChanges(): void; whenStable(): Promise<unknown> }) {
    for (let i = 0; i < 5; i++) await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
    await fixture.whenStable();
  }

  const title = (element: HTMLElement) => element.querySelector('#handover-title')?.textContent?.trim();

  // The first open used to read the required input in the constructor, which threw before any request
  // was sent, and the customer was told Khadra was not answering.
  it('asks the server for a code as soon as it opens', async () => {
    const fixture = await open('Pickup');

    const request = http.expectOne(URL);
    expect(request.request.method).toBe('POST');
    request.flush({ type: 'Pickup', code: '469258', qrPayload: 'khadra-handover:v1:KH-X:469258', expiresAt: new Date(Date.now() + 120_000).toISOString() });
    await settle(fixture);

    expect(fixture.nativeElement.querySelector('[role=alert]')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('469 258');
  });

  it('titles itself as a return before the server answers, and after it fails', async () => {
    const fixture = await open('Return');
    const element = fixture.nativeElement as HTMLElement;
    const returnTitle = title(element);

    http.expectOne(URL).flush({ code: 'server.error' }, { status: 503, statusText: 'Unavailable' });
    await settle(fixture);

    expect(title(element)).toBe(returnTitle);
    const pickup = await open('Pickup');
    http.expectOne(URL);
    expect(title(pickup.nativeElement)).not.toBe(returnTitle);
  });

  it('takes the handover from the server once it answers', async () => {
    const fixture = await open('Pickup');
    const pickupTitle = title(fixture.nativeElement);

    http.expectOne(URL).flush({ type: 'Return', code: '123456', qrPayload: 'khadra-handover:v1:KH-X:123456', expiresAt: new Date(Date.now() + 120_000).toISOString() });
    await settle(fixture);

    expect(title(fixture.nativeElement)).not.toBe(pickupTitle);
  });

  // Found at the counter: the customer pressed "new code" once too often, the limiter said 429, and
  // the panel threw away the code on screen, which the server had NOT replaced and still accepted.
  it('keeps the working code on screen when a new one is refused', async () => {
    const fixture = await open('Return');
    http.expectOne(URL).flush({ type: 'Return', code: '426988', qrPayload: 'khadra-handover:v1:KH-X:426988', expiresAt: new Date(Date.now() + 120_000).toISOString() });
    await settle(fixture);

    const again = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.handover button.btn--sm')!;
    again.click();
    await settle(fixture);
    http.expectOne(URL).flush({ code: 'rate_limited' }, { status: 429, statusText: 'Too Many Requests' });
    await settle(fixture);

    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('426 988');
    expect(element.querySelector('[role=alert]')).not.toBeNull();
  });

  it('takes the code away once the booking is no longer at a handover', async () => {
    const fixture = await open('Return');
    http.expectOne(URL).flush({ code: 'handover.not_available' }, { status: 409, statusText: 'Conflict' });
    await settle(fixture);

    expect(fixture.nativeElement.querySelector('.handover__digits')).toBeNull();
  });
});

/** The website's pickup code panel draws its QR whichever way the bundler hands the library over (E2E F29). */
describe('HandoverCodeComponent — the QR', () => {
  let http: HttpTestingController;
  const working = qrDraw.draw;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HandoverCodeComponent],
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    qrDraw.draw = working;
  });

  async function openAndAnswer() {
    const fixture = TestBed.createComponent(HandoverCodeComponent);
    fixture.componentRef.setInput('bookingId', BOOKING);
    fixture.componentRef.setInput('expected', 'Pickup');
    fixture.detectChanges();
    await fixture.whenStable();
    http.expectOne(URL).flush({
      type: 'Pickup',
      code: '469258',
      qrPayload: 'khadra-handover:v1:KH-X:469258',
      expiresAt: new Date(Date.now() + 120_000).toISOString(),
    });
    for (let i = 0; i < 5; i++) await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('draws the QR of the payload the server issued, from a library handed over under `default`', async () => {
    const element = await openAndAnswer();

    const image = element.querySelector('img.handover__qr') as HTMLImageElement | null;
    expect(image?.getAttribute('src')).toBe(`data:image/png;base64,${btoa('khadra-handover:v1:KH-X:469258')}`);
    expect(element.querySelector('[role=alert]')).toBeNull();
  });

  it('never blames the server when only the QR could not be drawn — the digits stay, and say they work', async () => {
    qrDraw.draw = async () => {
      throw new Error('No canvas in this browser.');
    };

    const element = await openAndAnswer();

    expect(element.querySelector('[role=alert]')).toBeNull();
    expect(element.querySelector('img.handover__qr')).toBeNull();
    expect(element.textContent).toContain('469 258');
    // The site opens in Arabic by default; the note says the six digits still work, in the language on screen.
    expect(element.textContent).toMatch(/six digits|الأرقام الستة/);
  });

  it('picks the library out of either shape, and refuses a module that has none', () => {
    const library = { toDataURL: async () => 'data:,' };
    expect(qrLibrary(library)).toBe(library);
    expect(qrLibrary({ default: library })).toBe(library);
    expect(qrLibrary({ default: {} })).toBeNull();
    expect(qrLibrary(undefined)).toBeNull();
  });
});

/**
 * Since Wave 7 the server issues a code only inside its window (pre-launch item 225) and refuses earlier with
 * `booking.pickup_too_early` / `booking.return_too_early`, naming the moment as `availableFrom`. The booking page
 * offers the code from the server's own moment, so only a browser clock running ahead gets here, and the panel
 * says when rather than a bare "not yet".
 */
describe('HandoverCodeComponent — asked for before its window', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HandoverCodeComponent],
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    });
    TestBed.inject(I18nService).use('en');
    http = TestBed.inject(HttpTestingController);
  });

  async function refusedWith(expected: 'Pickup' | 'Return', code: string, availableFrom: string | null) {
    const fixture = TestBed.createComponent(HandoverCodeComponent);
    fixture.componentRef.setInput('bookingId', BOOKING);
    fixture.componentRef.setInput('expected', expected);
    fixture.detectChanges();
    await fixture.whenStable();
    http.expectOne(URL).flush({ code, availableFrom }, { status: 409, statusText: 'Conflict' });
    for (let i = 0; i < 5; i++) await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    return {
      alert: element.querySelector('[role=alert]')?.textContent?.replace(/[⁨⁩]/g, '').trim() ?? '',
      digits: element.querySelector('.handover__digits'),
      when: TestBed.inject(FormatService).dateTime(availableFrom).replace(/[⁨⁩]/g, ''),
    };
  }

  it('names the moment a pickup code becomes available', async () => {
    const shown = await refusedWith('Pickup', 'booking.pickup_too_early', '2026-10-12T07:00:00+00:00');

    expect(shown.alert).toContain('Your pickup code will be available from');
    expect(shown.alert).toContain(shown.when);
    expect(shown.digits).toBeNull();
  });

  it('names the moment a return code becomes available', async () => {
    const shown = await refusedWith('Return', 'booking.return_too_early', '2026-10-12T09:00:00+00:00');

    expect(shown.alert).toContain('Your return code will be available from');
    expect(shown.alert).toContain(shown.when);
  });

  it('falls back to the general wording when the server names no moment', async () => {
    const shown = await refusedWith('Pickup', 'booking.pickup_too_early', null);

    expect(shown.alert).not.toContain('will be available from');
    expect(shown.alert).not.toBe('');
  });
});
