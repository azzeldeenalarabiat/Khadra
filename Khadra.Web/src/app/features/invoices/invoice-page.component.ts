import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FinancialDocumentPage } from '../../core/api/financial-documents.api';
import { httpData } from '../../core/http/http-data';
import { snapshotProblem } from '../../core/http/problem';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { SeoService } from '../../core/seo/seo.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { StatePanelComponent } from '../../shared/state/state-panel.component';
import { InvoiceContentComponent } from './invoice-content.component';
import { invoicePage } from './invoice-presentation';

const DOCUMENT_ID = /^[0-9a-f-]{36}$/i;

/**
 * One issued financial document (payments Phase 5b), at `/{lang}/invoices/{documentId}` — an address
 * Phase 7 will email, so it never moves. The stored document in the page's language; around it, its
 * standing, a void or a newer version, its other versions and the receipts it belongs with; and Print,
 * which prints the document and its standing alone until Phase 6 brings the PDF.
 *
 * An id that is not one, and a document that is not the reader's, read exactly alike: nothing here says
 * whether it exists.
 */
@Component({
  selector: 'kh-invoice-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, IconComponent, StatePanelComponent, InvoiceContentComponent],
  templateUrl: './invoice-page.component.html',
})
export class InvoicePageComponent {
  protected readonly i18n = inject(I18nService);
  private readonly format = inject(FormatService);
  // Not `document`: that name is this component's own word for what it shows.
  private readonly browserPage = inject(DOCUMENT);

  readonly documentId = input<string>('');

  protected readonly invoice = httpData<FinancialDocumentPage>(() => {
    const id = this.documentId();
    return DOCUMENT_ID.test(id) ? `/api/v1/financial-documents/${id}` : undefined;
  });
  protected readonly problem = computed(() => (this.invoice.error() ? snapshotProblem(this.invoice.error()) : null));
  protected readonly notFound = computed(
    () => !DOCUMENT_ID.test(this.documentId()) || this.problem()?.status === 404 || this.problem()?.status === 403,
  );
  protected readonly view = computed(() => {
    const page = this.invoice.value();
    return page ? invoicePage(page, this.i18n.isArabic(), this.i18n.t.bind(this.i18n), this.format) : null;
  });

  constructor() {
    inject(SeoService).set({ title: this.i18n.t('seo.invoices.title'), noindex: true });

    // A document the reader refused whole (docs/contracts/README.md) is reported once, for support: its id
    // and its schema version, never the snapshot — which holds a customer's name and their money.
    let reported: string | null = null;
    effect(() => {
      const page = this.invoice.value();
      const view = this.view();
      if (!page || !view || view.body || reported === page.documentId) return;
      reported = page.documentId;
      console.warn('A financial document could not be shown whole.', {
        documentId: page.documentId,
        snapshotSchemaVersion: page.snapshotSchemaVersion,
      });
    });
  }

  protected print(): void {
    this.browserPage.defaultView?.print();
  }
}
