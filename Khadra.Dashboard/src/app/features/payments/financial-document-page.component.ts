import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { problemMessage, serverSentence, snapshotProblem } from '../../core/i18n/problem';
import { AdminFinancialDocumentsService } from '../../core/services/admin-financial-documents.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { loaded } from '../../core/services/loaded';
import { FinancialDocumentComponent } from '../../shared/financial-document/financial-document.component';
import { IconComponent } from '../../shared/icon/icon.component';
import {
  DocumentFormat,
  DocumentWords,
  documentPage,
  refusalReport,
  voidDialogWords,
  voidRefusalIsFinal,
  voidedToast,
} from './financial-documents.presenter';

/**
 * One issued financial document, as the administrator reads it (payments Phase 5b): the document exactly
 * as issued, the facts it recorded, the proof of what was issued, its family and links, and its void when
 * there is one — with the one action this console takes on a document, voiding a wrong one, which issues
 * its correction in the same transaction.
 */
@Component({
  selector: 'kh-financial-document-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './financial-document-page.component.html',
  imports: [RouterLink, IconComponent, FinancialDocumentComponent],
})
export class FinancialDocumentPageComponent {
  private readonly i18n = inject(I18nService);
  protected readonly t = this.i18n.t;
  private readonly formats = inject(FormatService);
  private readonly service = inject(AdminFinancialDocumentsService);
  private readonly ui = inject(ConsoleUiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  private readonly documentId = toSignal(
    this.route.paramMap.pipe(map((params) => params.get('documentId'))),
    { initialValue: this.route.snapshot.paramMap.get('documentId') },
  );

  constructor() {
    effect(() => this.service.viewing.set(this.documentId()));

    // A document the reader refused whole is reported once, for support — its id and its schema version,
    // never the snapshot (refusalReport). The event name is for searching logs, not for a reader.
    let reported: string | null = null;
    effect(() => {
      const data = this.data();
      const report = data ? refusalReport(data.document) : null;
      if (!report || report.documentId === reported) return;
      reported = report.documentId;
      console.warn('financial-document-unreadable', report);
    });
  }

  protected readonly resource = this.service.document;
  private readonly data = loaded(this.resource);

  private readonly format: DocumentFormat = {
    money: (value) => this.formats.money(value.amount, value.currency),
    when: (iso) => this.formats.dateTimeSeconds(iso),
    relative: (iso) => this.formats.relative(iso),
    storedMoney: (amount, currency) => this.formats.storedMoney(amount, currency),
    frozenTime: (local) => this.formats.frozenTime(local),
  };

  protected readonly page = computed(() => {
    const data = this.data();
    if (!data) return null;
    const words: DocumentWords = {
      t: this.t,
      enumLabel: this.i18n.enumLabel,
      statusLabel: this.i18n.statusLabel,
      arabic: this.i18n.lang() === 'ar',
    };
    return documentPage(data, words, this.format);
  });

  /** A failed load: "no such document" for a 404, the server's sentence or a plain one otherwise. */
  protected readonly failure = computed(() => {
    const error = this.resource.error();
    if (!error) return null;
    const problem = snapshotProblem(error);
    if (problem.status === 404) return this.t('financialDocuments.notFound');
    return serverSentence(problem, this.i18n.lang(), this.t) ?? this.t('financialDocuments.pageLoadFailed');
  });

  protected reload(): void {
    this.resource.reload();
  }

  /**
   * Voids this document and issues its correction. The dialog states the consequence first; the reason is
   * a fixed field name, never a translated one. A refusal after which the document can never be current
   * again closes the dialog and reloads the page; any other leaves it open with the words.
   */
  protected voidDocument(): void {
    const page = this.page();
    if (!page?.canVoid) return;
    const words = voidDialogWords(page.number, page.statementFollows, this.t);
    this.ui.openAction(
      {
        icon: 'file-x',
        tone: 'bad',
        danger: true,
        title: words.title,
        body: words.body,
        note: words.note,
        fields: [{ name: 'reason', label: words.reasonLabel, type: 'text', placeholder: words.placeholder }],
        confirm: words.confirm,
        result: { title: this.t('financialDocuments.voidedTitle'), body: '', tone: 'ok' },
      },
      async (values) => {
        try {
          const voided = await this.service.void(page.id, values['reason'] ?? '');
          void this.router.navigate(['/payments/financial-documents', voided.replacementDocumentId]);
          return { ...voidedToast(voided.voidedNumber, voided.replacementNumber, this.t), tone: 'ok' };
        } catch (error) {
          const problem = snapshotProblem(error);
          if (!voidRefusalIsFinal(problem.code)) throw error;
          this.resource.reload();
          return {
            title: this.t('common.thatDidNotGoThrough'),
            body: problemMessage(problem, this.i18n.lang(), this.t) ?? this.t('common.serviceDidNotRespond'),
            tone: 'bad',
          };
        }
      },
      { title: this.t('financialDocuments.voidedTitle'), body: '' },
    );
  }
}
