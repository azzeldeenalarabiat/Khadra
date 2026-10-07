import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { afterEach, describe, expect, it } from 'vitest';

import { ConsentGateService } from '../session/consent-gate.service';
import { SessionService } from '../session/session.service';
import { ShortlistService } from './shortlist.service';

const CAR = '01a0cb9c-6bef-7dc5-bfe6-3cc9fc12702c';
const SAVE_URL = `/api/v1/customers/me/shortlist/${CAR}`;

@Component({ template: '' })
class PageComponent {}

describe('ShortlistService, a heart pressed before signing in (Wave 3 E5, E2E F12)', () => {
  const signedIn = signal(false);
  /** Whether the customer is known to owe no legal consent (Wave 4, W4-8). */
  const consentClear = signal(true);

  afterEach(() => TestBed.resetTestingModule());

  function setUp() {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: '**', component: PageComponent }]),
        { provide: SessionService, useValue: { isSignedIn: signedIn, user: signal(null) } },
        { provide: ConsentGateService, useValue: { clear: consentClear } },
      ],
    });
    const service = TestBed.inject(ShortlistService);
    return { service, http: TestBed.inject(HttpTestingController), router: TestBed.inject(Router) };
  }

  const settle = async () => {
    for (let i = 0; i < 5; i++) await new Promise((resolve) => setTimeout(resolve, 0));
    TestBed.tick();
  };

  it('saves the car once signed in, with a PUT, and the address forgets it', async () => {
    signedIn.set(true);
    const { service, http, router } = setUp();

    await router.navigateByUrl(`/en/cars?city=amman&save=${CAR}`);
    await settle();

    const put = http.expectOne(SAVE_URL);
    expect(put.request.method).toBe('PUT');
    put.flush(null, { status: 204, statusText: 'No Content' });
    await settle();
    expect(service.isSaved(CAR)).toBe(true);
    expect(router.url).toBe('/en/cars?city=amman');
  });

  it('waits until somebody is signed in before saving anything', async () => {
    signedIn.set(false);
    const { http, router } = setUp();

    await router.navigateByUrl(`/en/cars?save=${CAR}`);
    await settle();
    http.expectNone(SAVE_URL);

    signedIn.set(true);
    await settle();
    http.expectOne(SAVE_URL).flush(null, { status: 204, statusText: 'No Content' });
  });

  // Wave 4, W4-8: the save would only be refused while a legal text awaits the customer's consent.
  it('waits until the customer owes no legal consent, then saves the car', async () => {
    signedIn.set(true);
    consentClear.set(false);
    const { http, router } = setUp();

    await router.navigateByUrl(`/en/cars?save=${CAR}`);
    await settle();
    http.expectNone(SAVE_URL);

    consentClear.set(true);
    await settle();
    http.expectOne(SAVE_URL).flush(null, { status: 204, statusText: 'No Content' });
  });

  it('says the list is full when the server refuses, and saves nothing (E2E F70)', async () => {
    signedIn.set(true);
    const { service, http, router } = setUp();

    await router.navigateByUrl(`/en/cars?save=${CAR}`);
    await settle();
    http.expectOne(SAVE_URL).flush({ code: 'shortlist.full', title: 'Full.' }, { status: 409, statusText: 'Conflict' });
    await settle();

    expect(service.isSaved(CAR)).toBe(false);
    expect(service.lastRefusal()).toEqual({ vehicleId: CAR, code: 'shortlist.full' });
    service.dismissRefusal();
    expect(service.lastRefusal()).toBeNull();
  });

  it('ignores an address that names no car', async () => {
    signedIn.set(true);
    const { http, router } = setUp();

    await router.navigateByUrl('/en/cars?save=not-a-car');
    await settle();
    http.expectNone(() => true);
  });
});
