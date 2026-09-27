import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { describe, expect, it } from 'vitest';
import fixtureJson from '../../../../../docs/contracts/financial-documents-v1.json';
import { BookingFinancialDocuments } from '../../core/api/financial-documents.api';
import { I18nService } from '../../core/i18n/i18n.service';
import { BookingInvoicesComponent } from './booking-invoices.component';

/**
 * A booking's documents inside its "Payments & Invoices" card (payments Phase 5b), from the SHARED
 * contract fixture: each document linked, what is being prepared dated and worded by kind, and "Check
 * again" reading the list once more — no timer promising when.
 */
const documents = (fixtureJson as unknown as { bookingDocuments: BookingFinancialDocuments }).bookingDocuments;
const url = `/api/v1/bookings/${documents.bookingId}/financial-documents`;

async function render(language: 'ar' | 'en' = 'en') {
  TestBed.configureTestingModule({
    imports: [BookingInvoicesComponent],
    providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
  });
  TestBed.inject(I18nService).use(language);
  const http = TestBed.inject(HttpTestingController);
  const component = TestBed.createComponent(BookingInvoicesComponent);
  component.componentRef.setInput('bookingId', documents.bookingId);
  const settle = async () => {
    for (let i = 0; i < 5; i++) await new Promise((resolve) => setTimeout(resolve, 0));
    component.detectChanges();
  };
  await settle();
  return { http, settle, element: component.nativeElement as HTMLElement };
}

describe('BookingInvoicesComponent', () => {
  it('lists the booking’s documents and what is being prepared, with its date', async () => {
    const { http, settle, element } = await render();
    http.expectOne(url).flush(documents);
    await settle();
    const text = element.textContent!;
    expect(text).toContain('Invoices & Receipts');
    for (const document of documents.documents) expect(text).toContain(document.number);
    expect(text).toContain('Booking statement — being prepared');
    expect(element.querySelectorAll('a').length).toBe(documents.documents.length);
  });

  it('reads the list once more when asked to check again', async () => {
    const { http, settle, element } = await render();
    http.expectOne(url).flush(documents);
    await settle();
    element.querySelector<HTMLButtonElement>('.booking-invoices__preparing button')!.click();
    await settle();
    http.expectOne(url).flush({ ...documents, beingPrepared: [] });
    await settle();
    expect(element.textContent).not.toContain('being prepared');
  });

  it('shows nothing for a booking with nothing to list', async () => {
    const { http, settle, element } = await render();
    http.expectOne(url).flush({ ...documents, documents: [], beingPrepared: [] });
    await settle();
    expect(element.textContent!.trim()).toBe('');
  });

  it('says what is being prepared in Arabic', async () => {
    const { http, settle, element } = await render('ar');
    http.expectOne(url).flush(documents);
    await settle();
    expect(element.textContent).toContain('كشف حساب الحجز — قيد الإعداد');
    expect(element.textContent).toContain('تحقّق مجددًا');
  });
});
