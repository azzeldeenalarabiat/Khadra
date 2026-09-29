import { HttpClient, httpResource } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PagedResult } from '../models/bookings.api';
import {
  AdminFinancialDocument,
  AdminFinancialDocumentListItem,
  FinancialDocumentHold,
  FinancialDocumentVocabulary,
  RequestedFinancialDocumentEmail,
  SignedFileLink,
  VoidedFinancialDocument,
} from '../models/financial-documents.api';

/** Where every private file is served from: a minted link that points anywhere else is not followed. */
const PRIVATE_FILES = '/api/v1/documents/';

/** The documents list's filters, each a value the API reads as it is. */
export interface DocumentFilters {
  readonly type: string;
  readonly status: string;
  readonly number: string;
  readonly reference: string;
  /** Amman calendar days of issue, `yyyy-MM-dd`, inclusive at both ends. */
  readonly from: string;
  readonly to: string;
}

export const NO_DOCUMENT_FILTERS: DocumentFilters = {
  type: '',
  status: '',
  number: '',
  reference: '',
  from: '',
  to: '',
};

/**
 * The administrator's issued financial documents (payments Phase 5b): every receipt and statement, the
 * holds on documents owed and not issued, one document's page — and the one action, voiding a wrong
 * document, which issues its correction in the same transaction.
 */
@Injectable({ providedIn: 'root' })
export class AdminFinancialDocumentsService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/admin/financial-documents';

  readonly filters = signal<DocumentFilters>(NO_DOCUMENT_FILTERS);
  readonly page = signal(1);

  /** Only the filters that are set: an empty value would be a filter on nothing. */
  private readonly params = computed(() => {
    const params: Record<string, string | number> = { page: this.page(), pageSize: 25 };
    for (const [key, value] of Object.entries(this.filters())) {
      if (value.trim()) params[key] = value.trim();
    }
    return params;
  });

  /** Idle until a screen asks: nothing is read for a list nobody is looking at. */
  private readonly showingList = signal(false);
  private readonly showingHolds = signal(false);

  readonly documents = httpResource<PagedResult<AdminFinancialDocumentListItem>>(() =>
    this.showingList() ? { url: this.base, params: this.params() } : undefined,
  );

  /** The filter words, asked of the server once the list is on screen. */
  readonly vocabulary = httpResource<FinancialDocumentVocabulary>(() =>
    this.showingList() ? `${this.base}/vocabulary` : undefined,
  );

  readonly holdsPage = signal(1);

  readonly holds = httpResource<PagedResult<FinancialDocumentHold>>(() =>
    this.showingHolds()
      ? { url: `${this.base}/holds`, params: { page: this.holdsPage(), pageSize: 25 } }
      : undefined,
  );

  /** The document a page is showing; null keeps the resource idle. */
  readonly viewing = signal<string | null>(null);

  readonly document = httpResource<AdminFinancialDocument>(() => {
    const id = this.viewing();
    return id ? `${this.base}/${id}` : undefined;
  });

  /** Which list is on screen; the other stays idle. */
  show(view: 'list' | 'holds' | null): void {
    this.showingList.set(view === 'list');
    this.showingHolds.set(view === 'holds');
  }

  setFilters(filters: DocumentFilters): void {
    this.filters.set(filters);
    this.page.set(1);
  }

  /**
   * One PDF of a document (payments Phase 6), fetched through a link minted now — it lasts minutes — with the
   * bytes coming through the session like every private file: the document as issued (`AsIssued`), or a voided
   * document's voided copy (`Voided`). Rejects with the server's refusal.
   */
  async pdf(documentId: string, language: string, kind: string): Promise<Blob> {
    const link = await firstValueFrom(
      this.http.get<SignedFileLink>(`${this.base}/${documentId}/pdf-link`, { params: { language, kind } }),
    );
    if (!link.url.startsWith(PRIVATE_FILES)) throw { status: 0 };
    return firstValueFrom(this.http.get(link.url, { responseType: 'blob' }));
  }

  /**
   * Voids a CURRENT document and issues its correction under a new number, in one audited transaction.
   * Rejects with the server's refusal — which of them close the dialog is the page's decision.
   */
  async void(documentId: string, reason: string): Promise<VoidedFinancialDocument> {
    const token = await firstValueFrom(this.http.get<{ requestToken: string }>('/bff/antiforgery'));
    return firstValueFrom(
      this.http.post<VoidedFinancialDocument>(`${this.base}/${documentId}/void`, { reason }, {
        headers: { 'X-XSRF-TOKEN': token.requestToken },
      }),
    );
  }

  /**
   * Queues a receipt's email to its customer again, with its PDF (payments Phase 7), audited. The email service sends
   * it; the page's history shows when the mail provider accepts it. Rejects with the server's refusal.
   */
  async emailAgain(documentId: string): Promise<RequestedFinancialDocumentEmail> {
    const token = await firstValueFrom(this.http.get<{ requestToken: string }>('/bff/antiforgery'));
    return firstValueFrom(
      this.http.post<RequestedFinancialDocumentEmail>(`${this.base}/${documentId}/emails`, null, {
        headers: { 'X-XSRF-TOKEN': token.requestToken },
      }),
    );
  }
}
