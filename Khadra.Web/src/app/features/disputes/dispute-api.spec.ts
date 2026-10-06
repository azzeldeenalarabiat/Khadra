import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';

import { DisputeApi } from './dispute-api';

const BOOKING = '01a0d095-c204-7f23-b0b3-1be7852e4b23';
const TICKET = '01a10d05-f3a4-775a-930b-b6e48a40b512';

describe('DisputeApi', () => {
  let api: DisputeApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(DisputeApi);
    http = TestBed.inject(HttpTestingController);
  });

  const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

  it('uploads evidence the app\'s way: an upload address, then the file\'s own bytes with its declared type', async () => {
    const photo = new File([new Uint8Array([1, 2, 3])], 'scratch.jpg', { type: 'image/jpeg' });
    const attached = api.attach(BOOKING, photo);

    const ask = http.expectOne('/api/v1/disputes/evidence/upload-url');
    expect(ask.request.method).toBe('POST');
    expect(ask.request.body).toEqual({ bookingId: BOOKING, fileName: 'scratch.jpg', contentType: 'image/jpeg' });
    ask.flush({ uploadUrl: '/api/v1/uploads/tok', storageKey: `disputes/${BOOKING}/k.jpg`, expiresAt: '2026-10-06T08:15:00Z' });
    await settle();

    const put = http.expectOne('/api/v1/uploads/tok');
    expect(put.request.method).toBe('PUT');
    expect(put.request.headers.get('Content-Type')).toBe('image/jpeg');
    expect(put.request.body).toBe(photo);
    put.flush(null, { status: 204, statusText: 'No Content' });

    expect(await attached).toBe(`disputes/${BOOKING}/k.jpg`);
  });

  it('opens a dispute with the reason and the evidence keys, and nothing else', async () => {
    const opened = api.open(BOOKING, 'The office never handed the car over.', ['k1']);

    const request = http.expectOne('/api/v1/disputes');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ bookingId: BOOKING, reason: 'The office never handed the car over.', evidenceKeys: ['k1'] });
    request.flush({ ticketId: TICKET, slaDeadline: '2026-10-08T08:00:00Z' });

    expect((await opened).ticketId).toBe(TICKET);
  });

  it('adds a statement and withdraws a dispute at the ticket\'s own addresses', async () => {
    const added = api.addStatement(TICKET, 'Here is the photo.', ['k2']);
    const statement = http.expectOne(`/api/v1/disputes/${TICKET}/statements`);
    expect(statement.request.body).toEqual({ body: 'Here is the photo.', evidenceKeys: ['k2'] });
    statement.flush({});
    await added;

    const withdrawn = api.withdraw(TICKET);
    const withdraw = http.expectOne(`/api/v1/disputes/${TICKET}/withdraw`);
    expect(withdraw.request.method).toBe('POST');
    withdraw.flush({});
    await withdrawn;
  });
});
