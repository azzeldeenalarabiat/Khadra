import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, describe, expect, it } from 'vitest';

import { I18nService } from '../../core/i18n/i18n.service';
import { DisputeComponent } from './dispute.component';

const TICKET = '01a10d05-f3a4-775a-930b-b6e48a40b512';
const BOOKING = '01a0d095-c204-7f23-b0b3-1be7852e4b23';
const URL = `/api/v1/disputes/${TICKET}`;

const live = {
  ticketId: TICKET,
  bookingId: BOOKING,
  status: 'Open',
  isLive: true,
  openedByParty: 'Customer',
  reason: 'The office never handed the car over.',
  openedAt: '2026-10-05T17:04:00+00:00',
  slaDeadline: '2026-10-07T17:04:00+00:00',
  closedAt: null,
  statements: [
    {
      statementId: 's-1',
      party: 'Dealer',
      body: 'We waited at the counter.',
      createdAt: '2026-10-05T18:00:00+00:00',
      evidence: [{ fileName: 'counter.jpg', url: '/api/v1/documents/signed-1' }],
    },
  ],
  resolution: null,
  booking: { reference: 'KH-8GGUXMYD', refunds: [] },
};

describe('DisputeComponent, answering a live dispute (Wave 3 C4)', () => {
  afterEach(() => TestBed.resetTestingModule());

  async function render(dispute: object) {
    TestBed.configureTestingModule({
      imports: [DisputeComponent],
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    TestBed.inject(I18nService).use('en');
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(DisputeComponent);
    fixture.componentRef.setInput('ticketId', TICKET);
    const settle = async () => {
      for (let i = 0; i < 5; i++) await new Promise((resolve) => setTimeout(resolve, 0));
      fixture.detectChanges();
    };
    await settle();
    http.match((request) => request.url === URL).forEach((read) => read.flush(dispute));
    await settle();
    const page = fixture.nativeElement as HTMLElement;
    const button = (label: string) =>
      [...page.querySelectorAll('button')].find((b) => b.textContent?.trim() === label) as HTMLButtonElement | undefined;
    return { http, page, settle, button, text: () => page.textContent ?? '' };
  }

  it('no longer sends the customer to the app, and offers to add to the dispute', async () => {
    const { text, button } = await render(live);

    expect(text()).not.toContain('use the Khadra app');
    expect(text()).toContain('Add to your dispute');
    expect(button('Add it')).toBeDefined();
  });

  it('links each statement\'s evidence', async () => {
    const { page } = await render(live);

    const link = [...page.querySelectorAll('a')].find((a) => a.textContent?.includes('counter.jpg'));
    expect(link?.getAttribute('href')).toBe('/api/v1/documents/signed-1');
    expect(link?.getAttribute('rel')).toBe('noopener');
  });

  it('adds a statement, then reads the dispute again', async () => {
    const { page, http, settle, button, text } = await render(live);
    const box = page.querySelector('textarea') as HTMLTextAreaElement;
    box.value = 'Here is my receipt.';
    box.dispatchEvent(new Event('input'));
    await settle();

    button('Add it')!.click();
    await settle();
    const add = http.expectOne(`${URL}/statements`);
    expect(add.request.body).toEqual({ body: 'Here is my receipt.', evidenceKeys: [] });
    add.flush({});
    await settle();

    http.expectOne(URL).flush(live);
    await settle();
    expect(text()).toContain('Added. Khadra and the rental office can read it.');
  });

  it('asks before withdrawing a dispute the customer opened, then withdraws it', async () => {
    const { http, settle, button, text } = await render(live);

    button('Withdraw the dispute')!.click();
    await settle();
    expect(text()).toContain('The booking then settles as if no dispute had been raised.');
    http.expectNone(`${URL}/withdraw`);

    button('Withdraw it')!.click();
    await settle();
    http.expectOne(`${URL}/withdraw`).flush({});
    await settle();
    http.expectOne(URL).flush({ ...live, status: 'Withdrawn', isLive: false, closedAt: '2026-10-05T18:30:00+00:00' });
    await settle();
    expect(button('Withdraw the dispute')).toBeUndefined();
  });

  it('offers no withdrawal on a dispute the office opened, and nothing at all once it is closed', async () => {
    const office = await render({ ...live, openedByParty: 'Dealer' });
    expect(office.button('Withdraw the dispute')).toBeUndefined();
    expect(office.button('Add it')).toBeDefined();
    TestBed.resetTestingModule();

    const closed = await render({ ...live, status: 'Resolved', isLive: false, closedAt: '2026-10-05T20:25:00+00:00' });
    expect(closed.button('Add it')).toBeUndefined();
    expect(closed.button('Withdraw the dispute')).toBeUndefined();
  });
});
