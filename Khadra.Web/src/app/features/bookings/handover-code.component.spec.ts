import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { HandoverCodeComponent } from './handover-code.component';

const BOOKING = '01a0d082-8706-7748-af32-79cb5883e591';
const URL = `/api/v1/bookings/${BOOKING}/handover-code`;

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
});
