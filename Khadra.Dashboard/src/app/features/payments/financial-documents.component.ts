import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { serverSentence, snapshotProblem } from '../../core/i18n/problem';
import {
  AdminFinancialDocumentsService,
  DocumentFilters,
  NO_DOCUMENT_FILTERS,
} from '../../core/services/admin-financial-documents.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { DocumentFormat, DocumentWords, documentRow } from './financial-documents.presenter';

/**
 * Every issued financial document on the platform (payments Phase 5b): receipts and statements, every
 * version, newest issued first, filtered by the domain's own words and by Amman issue days. A tab of the
 * Payments screen. Read-only here: a document is voided from its own page, where its consequence is
 * stated first.
 */
@Component({
  selector: 'kh-financial-documents',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './financial-documents.component.html',
  imports: [RouterLink, IconComponent],
})
export class FinancialDocumentsComponent {
  private readonly i18n = inject(I18nService);
  protected readonly t = this.i18n.t;
  private readonly formats = inject(FormatService);
  private readonly service = inject(AdminFinancialDocumentsService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  constructor() {
    this.service.show('list');
    inject(DestroyRef).onDestroy(() => this.service.show(null));

    // A link from another screen arrives with its filter — a booking's "All documents for this
    // booking". Read once on entry, as Payments does; anything the link does not name starts clear.
    const query = this.route.snapshot.queryParamMap;
    const linked = ['type', 'status', 'number', 'reference'].some((key) => query.has(key));
    if (linked) {
      this.service.setFilters({
        ...NO_DOCUMENT_FILTERS,
        type: query.get('type') ?? '',
        status: query.get('status') ?? '',
        number: query.get('number') ?? '',
        reference: query.get('reference') ?? '',
      });
    }
  }

  private readonly words = computed<DocumentWords>(() => ({
    t: this.t,
    enumLabel: this.i18n.enumLabel,
    statusLabel: this.i18n.statusLabel,
    arabic: this.i18n.lang() === 'ar',
  }));

  private readonly format: DocumentFormat = {
    money: (value) => this.formats.money(value.amount, value.currency),
    // With the year: an archive that spans years, unlike the Payments list's working queue.
    when: (iso) => this.formats.dateTime(iso),
    relative: (iso) => this.formats.relative(iso),
    storedMoney: (amount, currency) => this.formats.storedMoney(amount, currency),
    frozenTime: (local) => this.formats.frozenTime(local),
  };

  protected readonly vocabulary = loaded(this.service.vocabulary);
  protected readonly filters = this.service.filters;
  protected readonly page = this.service.page;
  protected readonly resource = this.service.documents;
  private readonly data = loaded(this.resource);

  protected readonly rows = computed(() => {
    const words = this.words();
    return (this.data()?.items ?? []).map((row) => documentRow(row, words, this.format));
  });
  protected readonly total = computed(() => this.data()?.totalCount ?? 0);
  protected readonly pages = computed(() => this.data()?.totalPages ?? 1);
  protected readonly filtered = computed(() => Object.values(this.filters()).some((value) => value.trim() !== ''));

  protected readonly typeOptions = computed(() =>
    (this.vocabulary()?.types ?? []).map((name) => ({ name, label: this.i18n.enumLabel('financialDocumentType', name) })),
  );
  protected readonly statusOptions = computed(() =>
    (this.vocabulary()?.statuses ?? []).map((name) => ({
      name,
      label: this.i18n.statusLabel(name, 'financialDocument'),
    })),
  );

  /** A failed load, held as facts and worded here, so a language switch re-words it. */
  protected readonly failure = computed(() => {
    const error = this.resource.error();
    if (!error) return null;
    return serverSentence(snapshotProblem(error), this.i18n.lang(), this.t) ?? this.t('financialDocuments.loadFailed');
  });

  protected setFilter(key: keyof DocumentFilters, event: Event): void {
    const value = (event.target as HTMLInputElement | HTMLSelectElement).value;
    this.service.setFilters({ ...this.filters(), [key]: value });
  }

  protected clearFilters(): void {
    this.service.setFilters(NO_DOCUMENT_FILTERS);
  }

  protected goTo(page: number): void {
    this.page.set(Math.max(1, page));
  }

  protected open(documentId: string): void {
    void this.router.navigate(['/payments/financial-documents', documentId]);
  }

  protected reload(): void {
    this.resource.reload();
  }
}
