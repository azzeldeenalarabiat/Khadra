import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { serverSentence, snapshotProblem } from '../../core/i18n/problem';
import { AdminFinancialDocumentsService } from '../../core/services/admin-financial-documents.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { DocumentFormat, DocumentWords, holdRow } from './financial-documents.presenter';

/**
 * Documents owed and not issued, and why (payments Phase 5b): the records contradict one another, the
 * issuer is not configured, or a snapshot could not be composed. Never silence — each hold is tried
 * again on its own schedule, and this is where an administrator sees what is waiting and for how long.
 * The dashboard's queue row opens here, by path alone.
 */
@Component({
  selector: 'kh-financial-document-holds',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './financial-document-holds.component.html',
  imports: [RouterLink, IconComponent],
})
export class FinancialDocumentHoldsComponent {
  private readonly i18n = inject(I18nService);
  protected readonly t = this.i18n.t;
  private readonly formats = inject(FormatService);
  private readonly service = inject(AdminFinancialDocumentsService);

  constructor() {
    this.service.show('holds');
    inject(DestroyRef).onDestroy(() => this.service.show(null));
  }

  private readonly format: DocumentFormat = {
    money: (value) => this.formats.money(value.amount, value.currency),
    when: (iso) => this.formats.dateTime(iso),
    relative: (iso) => this.formats.relative(iso),
    storedMoney: (amount, currency) => this.formats.storedMoney(amount, currency),
    frozenTime: (local) => this.formats.frozenTime(local),
  };

  protected readonly page = this.service.holdsPage;
  protected readonly resource = this.service.holds;
  private readonly data = loaded(this.resource);

  protected readonly rows = computed(() => {
    const words: DocumentWords = {
      t: this.t,
      enumLabel: this.i18n.enumLabel,
      statusLabel: this.i18n.statusLabel,
      arabic: this.i18n.lang() === 'ar',
    };
    return (this.data()?.items ?? []).map((hold) => holdRow(hold, words, this.format));
  });
  protected readonly total = computed(() => this.data()?.totalCount ?? 0);
  protected readonly pages = computed(() => this.data()?.totalPages ?? 1);

  protected readonly failure = computed(() => {
    const error = this.resource.error();
    if (!error) return null;
    return serverSentence(snapshotProblem(error), this.i18n.lang(), this.t) ?? this.t('financialDocuments.holdsLoadFailed');
  });

  protected goTo(page: number): void {
    this.page.set(Math.max(1, page));
  }

  protected reload(): void {
    this.resource.reload();
  }
}
