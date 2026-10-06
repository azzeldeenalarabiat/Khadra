import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { I18nService } from '../../core/i18n/i18n.service';
import { OpenDisputeComponent } from './open-dispute.component';

const ID = '01a0d095-c204-7f23-b0b3-1be7852e4b23';
const TICKET = '01a10d05-f3a4-775a-930b-b6e48a40b512';
const BOOKING_URL = `/api/v1/bookings/${ID}`;

const disputable = {
  bookingId: ID,
  reference: 'KH-8GGUXMYD',
  status: 'Cancelled',
  canBeDisputed: true,
  liveDisputeId: null,
  disputeWindowEndsAt: '2026-10-07T17:01:00+00:00',
};

describe('OpenDisputeComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  async function render(booking: object, language: 'ar' | 'en' = 'en') {
    TestBed.configureTestingModule({
      imports: [OpenDisputeComponent],
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    TestBed.inject(I18nService).use(language);
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(OpenDisputeComponent);
    fixture.componentRef.setInput('bookingId', ID);
    const settle = async () => {
      for (let i = 0; i < 5; i++) await new Promise((resolve) => setTimeout(resolve, 0));
      fixture.detectChanges();
    };
    await settle();
    http.match((request) => request.url === BOOKING_URL).forEach((read) => read.flush(booking));
    await settle();
    const page = fixture.nativeElement as HTMLElement;
    return { fixture, http, page, settle, text: () => page.textContent ?? '' };
  }

  it('offers the form while the server allows a dispute, naming the window and what happens to the money', async () => {
    const { page, text } = await render(disputable);

    expect(text()).toContain('Open a dispute');
    expect(text()).toContain('KH-8GGUXMYD');
    expect(text()).toContain('Nothing is charged to you, and nothing held on this booking is released, until Khadra decides.');
    expect(text()).toContain('You can open a dispute on this booking until');
    expect(page.querySelector('textarea')).not.toBeNull();
  });

  it('links the open dispute instead of a second form, as the server allows only one', async () => {
    const { page, text } = await render({ ...disputable, liveDisputeId: TICKET });

    expect(text()).toContain('A dispute is already open on this booking.');
    expect(page.querySelector('textarea')).toBeNull();
    expect([...page.querySelectorAll('a')].map((a) => a.getAttribute('href'))).toContain(`/en/disputes/${TICKET}`);
  });

  it('says plainly when no dispute can be opened now', async () => {
    const { page, text } = await render({ ...disputable, canBeDisputed: false });

    expect(text()).toContain('A dispute cannot be opened on this booking now.');
    expect(page.querySelector('textarea')).toBeNull();
  });

  it('asks for the reason before sending anything', async () => {
    const { page, http, settle, text } = await render(disputable);

    (page.querySelector('button.btn--primary') as HTMLButtonElement).click();
    await settle();

    expect(text()).toContain('Say what went wrong first.');
    http.expectNone('/api/v1/disputes');
  });

  it('opens the dispute with the reason and goes to it', async () => {
    const { page, http, settle } = await render(disputable);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const box = page.querySelector('textarea') as HTMLTextAreaElement;
    box.value = 'The office never handed the car over.';
    box.dispatchEvent(new Event('input'));
    await settle();

    (page.querySelector('button.btn--primary') as HTMLButtonElement).click();
    await settle();
    const open = http.expectOne('/api/v1/disputes');
    expect(open.request.body).toEqual({ bookingId: ID, reason: 'The office never handed the car over.', evidenceKeys: [] });
    open.flush({ ticketId: TICKET, slaDeadline: '2026-10-08T08:00:00Z' });
    await settle();

    expect(navigate).toHaveBeenCalledWith(['/', 'en', 'disputes', TICKET], { state: { opened: true } });
  });

  it('words the server\'s refusal in Arabic, never in English', async () => {
    const { page, http, settle, text } = await render(disputable, 'ar');
    const box = page.querySelector('textarea') as HTMLTextAreaElement;
    box.value = 'لم يسلّمني المكتب السيارة.';
    box.dispatchEvent(new Event('input'));
    await settle();

    (page.querySelector('button.btn--primary') as HTMLButtonElement).click();
    await settle();
    http.expectOne('/api/v1/disputes').flush(
      { code: 'dispute.booking_not_disputable', title: 'The dispute window has closed.' },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();

    expect(text()).toContain('لا يمكن فتح نزاع على هذا الحجز الآن.');
    expect(text()).not.toContain('The dispute window has closed.');
  });
});
