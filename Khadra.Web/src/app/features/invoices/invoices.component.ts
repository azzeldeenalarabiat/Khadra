import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Paged } from '../../core/api/common.api';
import { FINANCIAL_DOCUMENT_TYPES, FinancialDocumentRow } from '../../core/api/financial-documents.api';
import { httpData } from '../../core/http/http-data';
import { snapshotProblem } from '../../core/http/problem';
import { TranslationKey } from '../../core/i18n/en';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { SeoService } from '../../core/seo/seo.service';
import { StatePanelComponent } from '../../shared/state/state-panel.component';
import { invoiceRow } from './invoice-presentation';

const PAGE_SIZE = 10;

/** The filter tabs: every document, or one kind of it. */
export const INVOICE_FILTERS = ['all', ...FINANCIAL_DOCUMENT_TYPES] as const;
export type InvoiceFilter = (typeof INVOICE_FILTERS)[number];

/**
 * Invoices & Receipts (payments Phase 5b; owner, 2026-09-27): every financial document the customer has,
 * newest issued first as the server orders them — every version, earlier and voided ones included, each
 * marked, because an issued record never disappears from its owner's account.
 */
@Component({
  selector: 'kh-invoices',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, StatePanelComponent],
  templateUrl: './invoices.component.html',
})
export class InvoicesComponent {
  protected readonly i18n = inject(I18nService);
  private readonly format = inject(FormatService);
  protected readonly filters = INVOICE_FILTERS;

  private readonly query = toSignal(inject(ActivatedRoute).queryParamMap);
  /** A kind this page does not offer reads as All: the server refuses an unknown type, and a mistyped link should not open on an error. */
  protected readonly filter = computed<InvoiceFilter>(() => {
    const value = this.query()?.get('type') ?? '';
    return (INVOICE_FILTERS as readonly string[]).includes(value) ? (value as InvoiceFilter) : 'all';
  });
  protected readonly page = computed(() => {
    const value = Number(this.query()?.get('page'));
    return Number.isInteger(value) && value > 1 ? value : 1;
  });

  protected readonly documents = httpData<Paged<FinancialDocumentRow>>(() => ({
    url: '/api/v1/customers/me/financial-documents',
    params: { ...(this.filter() !== 'all' ? { type: this.filter() } : {}), page: this.page(), pageSize: PAGE_SIZE },
  }));
  protected readonly problem = computed(() => (this.documents.error() ? snapshotProblem(this.documents.error()) : null));
  protected readonly rows = computed(() =>
    (this.documents.value()?.items ?? []).map((row) =>
      invoiceRow(row, this.i18n.isArabic(), this.i18n.t.bind(this.i18n), this.format),
    ),
  );

  constructor() {
    inject(SeoService).set({ title: this.i18n.t('seo.invoices.title'), noindex: true });
  }

  protected filterLabel(filter: InvoiceFilter): string {
    return this.i18n.t(`invoices.filter.${filter}` as TranslationKey);
  }

  protected filterParams(filter: InvoiceFilter): Record<string, string> {
    return filter === 'all' ? {} : { type: filter };
  }

  protected pageParams(page: number): Record<string, string> {
    return { ...this.filterParams(this.filter()), ...(page > 1 ? { page: String(page) } : {}) };
  }
}
