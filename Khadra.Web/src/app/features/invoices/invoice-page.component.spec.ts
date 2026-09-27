import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import fixtureJson from '../../../../../docs/contracts/financial-documents-v1.json';
import { FinancialDocumentPage } from '../../core/api/financial-documents.api';
import { I18nService } from '../../core/i18n/i18n.service';
import { InvoicePageComponent } from './invoice-page.component';

/**
 * One document's page (payments Phase 5b), rendering documents from the SHARED contract fixture. What
 * matters most here is what the page does around the document: nothing says whether a document it cannot
 * show exists, and printing keeps the document's standing — a printed voided receipt without its notice
 * would read as a valid one.
 */
interface ContractFixture {
  readonly documents: readonly { readonly name: string; readonly page: FinancialDocumentPage }[];
}
const fixture = fixtureJson as unknown as ContractFixture;
const page = (name: string): FinancialDocumentPage => structuredClone(fixture.documents.find((entry) => entry.name === name)!.page);
const url = (id: string) => `/api/v1/financial-documents/${id}`;

async function render(id: string, language: 'ar' | 'en' = 'en') {
  TestBed.configureTestingModule({
    imports: [InvoicePageComponent],
    providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
  });
  TestBed.inject(I18nService).use(language);
  const http = TestBed.inject(HttpTestingController);
  const component = TestBed.createComponent(InvoicePageComponent);
  component.componentRef.setInput('documentId', id);
  const settle = async () => {
    for (let i = 0; i < 5; i++) await new Promise((resolve) => setTimeout(resolve, 0));
    component.detectChanges();
  };
  await settle();
  return { http, settle, element: component.nativeElement as HTMLElement };
}

async function show(document: FinancialDocumentPage, language: 'ar' | 'en' = 'en') {
  const rendered = await render(document.documentId, language);
  rendered.http.expectOne(url(document.documentId)).flush(document);
  await rendered.settle();
  return rendered;
}

describe('InvoicePageComponent', () => {
  afterEach(() => vi.restoreAllMocks());

  it('shows the stored document whole, with the notice it stored', async () => {
    const { element } = await show(page('payment-receipt-paid-in-full'));
    const text = element.textContent!;
    expect(text).toContain('Payment receipt');
    expect(text).toContain('TEST-PAY-2026-000002');
    expect(text).toContain('Card processing fee, included');
    expect(text).toContain('This document is not a tax invoice.');
    expect(element.querySelector('.invoice')).not.toBeNull();
  });

  it('shows the document in Arabic from the same answer', async () => {
    const { element } = await show(page('payment-receipt-paid-in-full'), 'ar');
    expect(element.textContent).toContain('إيصال دفع');
    expect(element.textContent).toContain('هذا المستند ليس فاتورة ضريبية.');
  });

  it('prints the document with its standing and its notice, and none of the page around it', async () => {
    const { element } = await show(page('payment-receipt-deposit-voided'));
    const notice = element.querySelector('.invoice-page__notice')!;
    expect(notice.textContent).toContain('Voided on');
    expect(notice.textContent).toContain('Replaced by TEST-PAY-2026-000005.');
    // Printed: the standing, the notice and the document itself.
    expect(notice.closest('.no-print')).toBeNull();
    expect(element.querySelector('.invoice-page__meta .badge')!.closest('.no-print')).toBeNull();
    expect(element.querySelector('.invoice')!.closest('.no-print')).toBeNull();
    // Not printed: the way back, Print itself, the booking link and the other documents.
    expect(element.querySelector('.invoice-page__back')!.classList).toContain('no-print');
    expect(element.querySelector('button')!.closest('.no-print')).not.toBeNull();
    expect(element.querySelector('.invoice-page__related')!.classList).toContain('no-print');
  });

  it('prints from the browser when asked', async () => {
    const print = vi.spyOn(window, 'print').mockImplementation(() => undefined);
    const { element } = await show(page('refund-receipt-free-cancellation'));
    element.querySelector<HTMLButtonElement>('.invoice-page__head button')!.click();
    expect(print).toHaveBeenCalledOnce();
  });

  it('links an earlier version to the newest', async () => {
    const { element } = await show(page('booking-statement-superseded'));
    expect(element.querySelector('.invoice-page__notice a')!.textContent).toContain('A newer version exists: TEST-STM-2026-000003');
  });

  // Left to wrap with its sentence, a number broke at a hyphen on a phone ("TEST-PAY-" / "2026-000005"):
  // each is now a span of its own, which the stylesheet keeps from wrapping.
  it('keeps the payment receipt number whole in "Issued against payment receipt …"', async () => {
    const { element } = await show(page('refund-receipt-free-cancellation'));
    expect(element.querySelector('.invoice-page__related a .ltr')!.textContent).toBe('TEST-PAY-2026-000002');
    expect(element.querySelector('.invoice-page__related a')!.textContent).toBe('Issued against payment receipt TEST-PAY-2026-000002');
  });

  it('keeps the correction number whole in "Replaced by …"', async () => {
    const { element } = await show(page('payment-receipt-deposit-voided'));
    expect(element.querySelector('.invoice-page__notice a .ltr')!.textContent).toBe('TEST-PAY-2026-000005');
  });

  it('keeps the newest number whole in "A newer version exists: …"', async () => {
    const { element } = await show(page('booking-statement-superseded'));
    expect(element.querySelector('.invoice-page__notice a .ltr')!.textContent).toBe('TEST-STM-2026-000003');
  });

  it('says a document is not available, and nothing more, when the server does not find it', async () => {
    const id = '00000000-0000-4000-8000-000000000999';
    const { http, settle, element } = await render(id);
    http.expectOne(url(id)).flush({ code: 'financial_documents.not_found' }, { status: 404, statusText: 'Not Found' });
    await settle();
    expect(element.textContent).toContain("This document isn't available.");
    expect(element.textContent).toContain('Back to Invoices & Receipts');
  });

  it('asks for nothing when the address holds no document id, and says the same', async () => {
    const { http, element } = await render('not-a-document');
    expect(http.match(() => true)).toHaveLength(0);
    expect(element.textContent).toContain("This document isn't available.");
  });

  it('shows the facts it has and says so when it cannot show a document whole, reporting its id and version only', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    const unknown = { ...page('payment-receipt-paid-in-full'), snapshotSchemaVersion: 2 };
    const { element } = await show(unknown);
    expect(element.querySelector('.invoice')).toBeNull();
    expect(element.textContent).toContain("This document can't be shown here yet.");
    expect(element.textContent).toContain('Amount paid');
    expect(element.textContent).toContain('Issued');
    expect(element.textContent).toContain('TEST-PAY-2026-000002');

    expect(warn).toHaveBeenCalledOnce();
    expect(warn.mock.calls[0]![1]).toEqual({ documentId: unknown.documentId, snapshotSchemaVersion: 2 });
    // Never the snapshot: it holds a customer's name and their money.
    expect(JSON.stringify(warn.mock.calls)).not.toContain('Rana Sharif');
  });

  it('isolates a name registered in Arabic in its own direction, and a Latin literal left to right', async () => {
    const document = page('payment-receipt-paid-in-full');
    const content = (document.snapshot as { content: { sections: { key: string; lines: { key: string; plain?: string }[] }[] } }).content;
    content.sections.find((section) => section.key === 'parties')!.lines.find((line) => line.key === 'office')!.plain = 'أوتو رنت (Auto Rent)';
    const { element } = await show(document, 'ar');
    expect([...element.querySelectorAll('.invoice bdi')].map((node) => node.textContent)).toContain('أوتو رنت (Auto Rent)');
    expect([...element.querySelectorAll('.invoice .ltr')].map((node) => node.textContent)).toContain('TEST-PAY-2026-000002');
  });
});
