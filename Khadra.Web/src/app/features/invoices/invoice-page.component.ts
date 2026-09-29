import { HttpClient } from '@angular/common/http';
import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { FinancialDocumentPage, SignedFileLink } from '../../core/api/financial-documents.api';
import { AppConfigService } from '../../core/config/app-config.service';
import { httpData } from '../../core/http/http-data';
import { snapshotProblem } from '../../core/http/problem';
import { problemText } from '../../core/http/problem-text';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { SeoService } from '../../core/seo/seo.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { StatePanelComponent } from '../../shared/state/state-panel.component';
import { InvoiceContentComponent } from './invoice-content.component';
import { invoicePage, PdfDownloadView } from './invoice-presentation';

const DOCUMENT_ID = /^[0-9a-f-]{36}$/i;

/** Where every private file is served from: a minted link that points anywhere else is not followed. */
const PRIVATE_FILES = '/api/v1/documents/';

/**
 * How long a downloaded PDF's object URL outlives the click. Revoked at once, some browsers cancel the
 * download they were handed; kept, it holds the file in memory until the tab closes.
 */
const OBJECT_URL_GRACE_MS = 30_000;

/**
 * One issued financial document (payments Phase 5b), at `/{lang}/invoices/{documentId}` — an address
 * Phase 7 will email, so it never moves. The stored document in the page's language; around it, its
 * standing, a void or a newer version, its other versions and the receipts it belongs with; Print, which
 * prints the document and its standing alone; and its PDFs (Phase 6), one per language drawn.
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
  private readonly http = inject(HttpClient);
  private readonly appConfig = inject(AppConfigService);
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

  /** The language of the PDF being fetched, while one is. */
  protected readonly downloading = signal<string | null>(null);
  /** Why the last PDF could not be fetched, in the page's language. */
  protected readonly pdfProblem = signal<string | null>(null);

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

  /**
   * Fetches one PDF and saves it under its document's number and language. The link is minted on the
   * click — it lasts minutes — and the bytes come through the session like every private file, so the
   * browser is handed a file of its own to save rather than an address that would stop working.
   */
  protected async downloadPdf(download: PdfDownloadView): Promise<void> {
    const page = this.invoice.value();
    const browser = this.browserPage.defaultView;
    if (!page || !browser || this.downloading()) return;

    this.downloading.set(download.language);
    this.pdfProblem.set(null);
    try {
      const link = await firstValueFrom(
        this.http.get<SignedFileLink>(`/api/v1/financial-documents/${page.documentId}/pdf-link`, {
          params: { language: download.language },
        }),
      );
      if (!link.url.startsWith(PRIVATE_FILES)) throw { status: 0 };
      const file = await firstValueFrom(this.http.get(link.url, { responseType: 'blob' }));
      const address = browser.URL.createObjectURL(file);
      const anchor = this.browserPage.createElement('a');
      anchor.href = address;
      anchor.download = download.fileName;
      anchor.rel = 'noopener';
      this.browserPage.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
      browser.setTimeout(() => browser.URL.revokeObjectURL(address), OBJECT_URL_GRACE_MS);
    } catch (error) {
      this.pdfProblem.set(
        problemText(snapshotProblem(error), this.i18n.t.bind(this.i18n), this.i18n.language(), this.appConfig.config()),
      );
    } finally {
      this.downloading.set(null);
    }
  }
}
