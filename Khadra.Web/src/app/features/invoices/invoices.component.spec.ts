import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { describe, expect, it } from 'vitest';
import fixtureJson from '../../../../../docs/contracts/financial-documents-v1.json';
import { Paged } from '../../core/api/common.api';
import { FinancialDocumentRow } from '../../core/api/financial-documents.api';
import { InvoicesComponent } from './invoices.component';

/**
 * Invoices & Receipts (payments Phase 5b), against the SHARED contract fixture's own list: every
 * version, each marked, and a filter that never turns a mistyped address into an error.
 */
const LIST = '/api/v1/customers/me/financial-documents';
const myDocuments = (fixtureJson as unknown as { myDocuments: Paged<FinancialDocumentRow> }).myDocuments;
const empty: Paged<FinancialDocumentRow> = { ...myDocuments, items: [], totalCount: 0, totalPages: 0, hasNext: false };

async function open(address: string) {
  TestBed.configureTestingModule({
    providers: [
      provideZonelessChangeDetection(),
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([{ path: 'invoices', component: InvoicesComponent }]),
    ],
  });
  const harness = await RouterTestingHarness.create();
  await harness.navigateByUrl(address, InvoicesComponent);
  const http = TestBed.inject(HttpTestingController);
  const settle = async () => {
    for (let i = 0; i < 5; i++) await new Promise((resolve) => setTimeout(resolve, 0));
    harness.detectChanges();
  };
  await settle();
  const request = (): TestRequest => http.expectOne((candidate) => candidate.url === LIST);
  return { harness, settle, request, element: () => harness.routeNativeElement as HTMLElement };
}

describe('InvoicesComponent', () => {
  it('lists every version, each marked, newest issued first as the server sent them', async () => {
    const { request, settle, element } = await open('/invoices');
    request().flush(myDocuments);
    await settle();
    const rows = [...element().querySelectorAll('.invoice-row')];
    expect(rows).toHaveLength(myDocuments.items.length);
    expect(rows[0]!.textContent).toContain(myDocuments.items[0]!.number);
    const voided = rows.find((row) => row.textContent!.includes('TEST-PAY-2026-000001'))!;
    expect(voided.querySelector('.badge')!.textContent).toContain('ملغى');
  });

  it('reads a kind it does not offer as All, rather than asking the server for it', async () => {
    const { request, settle, element } = await open('/invoices?type=Nonsense');
    const sent = request();
    expect(sent.request.params.has('type')).toBe(false);
    sent.flush(myDocuments);
    await settle();
    expect(element().querySelector('[role=tab][aria-selected=true]')!.textContent).toContain('الكل');
  });

  it('asks for one kind when one is chosen', async () => {
    const { request } = await open('/invoices?type=RefundReceipt&page=2');
    const sent = request();
    expect(sent.request.params.get('type')).toBe('RefundReceipt');
    expect(sent.request.params.get('page')).toBe('2');
  });

  it('promises nothing when there is nothing yet', async () => {
    const all = await open('/invoices');
    all.request().flush(empty);
    await all.settle();
    expect(all.element().textContent).toContain('لا توجد إيصالات أو كشوف حساب بعد.');
    TestBed.resetTestingModule();

    const kind = await open('/invoices?type=BookingStatement');
    kind.request().flush(empty);
    await kind.settle();
    expect(kind.element().textContent).toContain('لا يوجد شيء من هذا النوع بعد.');
  });
});
