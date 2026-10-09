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
    const { page, text } = await render(live);

    // The author and the time keep their space (E2E F76: "the rental office· 5 Oct").
    expect(text()).toContain('the rental office · ');

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

  it('says once that the dispute is open, and never above one that was withdrawn (E2E F78)', async () => {
    const opened = 'Your dispute is open. Khadra and the rental office can read it.';
    // What the opening page leaves in the history entry, beside the router's own key.
    window.history.replaceState({ opened: true, navigationId: 7 }, '');
    const first = await render(live);
    expect(first.text()).toContain(opened);
    expect(window.history.state).toEqual({ navigationId: 7 });

    first.button('Withdraw the dispute')!.click();
    await first.settle();
    first.button('Withdraw it')!.click();
    await first.settle();
    first.http.expectOne(`${URL}/withdraw`).flush({});
    await first.settle();
    first.http.expectOne(URL).flush({ ...live, status: 'Withdrawn', isLive: false, closedAt: '2026-10-05T18:30:00+00:00' });
    await first.settle();
    expect(first.text()).not.toContain(opened);
    TestBed.resetTestingModule();

    // The same entry reloaded: the browser keeps its state, but the flag was taken out when it was read.
    const reloaded = await render(live);
    expect(reloaded.text()).not.toContain(opened);
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

/**
 * A settled dispute shows the customer their own figures and nothing of the other parties' (owner decision 3;
 * pre-launch item 151). Since Wave 7 the server sends the office's and the platform's shares, the charge to the
 * office and the waiver flag as null to a customer; the page used to read the flag and sum the decision up as
 * "nothing is owed by either side", which was false whenever the office was charged.
 */
describe('DisputeComponent, a settled dispute (pre-launch item 151)', () => {
  afterEach(() => TestBed.resetTestingModule());

  const jod = (amount: number) => ({ amount, currency: 'JOD' });
  const settled = (refund: number, extra: object = {}) => ({
    ...live,
    status: 'Resolved',
    isLive: false,
    closedAt: '2026-10-06T09:00:00+00:00',
    resolution: {
      depositHeld: jod(18),
      refundToCustomer: jod(refund),
      retainedByPlatform: null,
      transferredToDealer: null,
      dealerCharge: null,
      waivesEverything: null,
      note: 'Split after reading both sides.',
      resolvedAt: '2026-10-06T09:00:00+00:00',
    },
    ...extra,
  });

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
    const rows = () =>
      [...page.querySelectorAll('#resolution-title ~ dl > div')].map((row) => ({
        term: row.querySelector('dt')?.textContent?.trim(),
        value: row.querySelector('dd')?.textContent?.replace(/[⁨⁩]/g, '').trim(),
      }));
    return { rows, text: () => page.textContent ?? '' };
  }

  it("shows the customer's own refund, and nothing the office or the platform kept or was charged", async () => {
    const { rows, text } = await render(settled(5));

    expect(rows()).toEqual([{ term: 'Refunded to you', value: expect.stringMatching(/JOD\s*5/) }]);
    expect(text()).toContain('Split after reading both sides.');
    expect(text()).not.toContain('Charged to the rental office');
    expect(text()).not.toContain('Nothing is owed by either side.');
  });

  it('shows a refund of nothing as a figure, never summed up for the other parties', async () => {
    const { rows, text } = await render(settled(0));

    expect(rows()).toEqual([{ term: 'Refunded to you', value: expect.stringMatching(/JOD\s*0/) }]);
    expect(text()).not.toContain('Nothing is owed by either side.');
  });

  it('puts what earlier disputes decided beside the refund, in the server figure', async () => {
    const { rows } = await render(settled(5, { decidedByEarlierTickets: jod(13) }));

    expect(rows()).toEqual([
      { term: 'Refunded to you', value: expect.stringMatching(/JOD\s*5/) },
      { term: 'Decided by earlier disputes', value: expect.stringMatching(/JOD\s*13/) },
    ]);
  });
});
