import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { problemMessage, serverSentence, snapshotProblem } from '../../core/i18n/problem';
import { AdminPaymentsService } from '../../core/services/admin-payments.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { DocumentFormat, DocumentWords, documentRow } from './financial-documents.presenter';
import { PaymentIncidentView, paymentPage } from './payment-detail.presenter';

/**
 * One payment (payments Phase 4b): the attempt as its booking's financial state describes it, its
 * refunds with booking money and fee apart, the booking it belongs to, and every event the provider
 * sent about it — including one that arrived before the payment's reference was saved, which only its
 * reference ties to it. The live checkout link is never on this page. Its one action closes a capture incident
 * with the administrator's account of it (Wave 4, B1), which moves no money.
 */
@Component({
  selector: 'kh-payment-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './payment-detail.component.html',
  imports: [RouterLink, IconComponent],
})
export class PaymentDetailComponent {
  private readonly i18n = inject(I18nService);
  protected readonly t = this.i18n.t;
  private readonly formats = inject(FormatService);
  private readonly service = inject(AdminPaymentsService);
  private readonly route = inject(ActivatedRoute);
  private readonly ui = inject(ConsoleUiService);

  private readonly paymentId = toSignal(
    this.route.paramMap.pipe(map((params) => params.get('paymentId'))),
    { initialValue: this.route.snapshot.paramMap.get('paymentId') },
  );

  constructor() {
    effect(() => this.service.viewing.set(this.paymentId()));
  }

  protected readonly resource = this.service.payment;
  private readonly data = loaded(this.resource);

  protected readonly page = computed(() => {
    const data = this.data();
    return data
      ? paymentPage(data, this.t, this.i18n.enumLabel, {
          money: (value) => this.formats.money(value.amount, value.currency),
          when: (iso) => this.formats.dateTimeSeconds(iso),
        })
      : null;
  });

  /**
   * The payment's receipts, every version (payments Phase 5b), each opening its own page. Null — and
   * nothing said — when the API sent none at all, as one without documents does.
   */
  protected readonly documents = computed(() => {
    const documents = this.data()?.documents;
    if (!documents) return null;
    const words: DocumentWords = {
      t: this.t,
      enumLabel: this.i18n.enumLabel,
      statusLabel: this.i18n.statusLabel,
      arabic: this.i18n.lang() === 'ar',
    };
    const format: DocumentFormat = {
      money: (value) => this.formats.money(value.amount, value.currency),
      when: (iso) => this.formats.dateTime(iso),
      relative: (iso) => this.formats.relative(iso),
      storedMoney: (amount, currency) => this.formats.storedMoney(amount, currency),
      frozenTime: (local) => this.formats.frozenTime(local),
      count: (value) => this.formats.number(value),
    };
    return documents.map((row) => documentRow(row, words, format));
  });

  /** A failed load: "no such payment" for a 404, the server's sentence or a plain one otherwise. */
  protected readonly failure = computed(() => {
    const error = this.resource.error();
    if (!error) return null;
    const problem = snapshotProblem(error);
    if (problem.status === 404) return this.t('paymentDetail.notFound');
    return serverSentence(problem, this.i18n.lang(), this.t) ?? this.t('paymentDetail.loadFailed');
  });

  protected reload(): void {
    this.resource.reload();
  }

  /**
   * Closes one capture incident with the administrator's account of how its money was dealt with at the provider
   * (Wave 4, B1). The dialog states that nothing moves; the note is required, and is audited against their name.
   */
  protected markHandled(incident: PaymentIncidentView): void {
    const paymentId = this.paymentId();
    if (!paymentId || !incident.open) return;
    this.ui.openAction(
      {
        icon: 'check-circle',
        tone: 'accent',
        title: this.t('paymentDetail.handle.title', { kind: incident.kind }),
        body: this.t('paymentDetail.handle.body'),
        fields: [{ name: 'note', label: this.t('paymentDetail.handle.note'), type: 'text' }],
        confirm: this.t('paymentDetail.handle.confirm'),
        result: { title: this.t('paymentDetail.handle.done'), body: '', tone: 'ok' },
      },
      async (values) => {
        try {
          await this.service.markIncidentHandled(paymentId, incident.id, values['note'] ?? '');
          this.resource.reload();
          return { title: this.t('paymentDetail.handle.done'), body: '', tone: 'ok' as const };
        } catch (error) {
          const problem = snapshotProblem(error);
          // Somebody else's account got there first: it stands, the page shows it, and this one is told so.
          if (problem.code !== 'payments.incident_already_handled') throw error;
          this.resource.reload();
          return {
            title: this.t('common.thatDidNotGoThrough'),
            body: problemMessage(problem, this.i18n.lang(), this.t) ?? this.t('common.serviceDidNotRespond'),
            tone: 'bad' as const,
          };
        }
      },
      { title: this.t('paymentDetail.handle.done'), body: '' },
    );
  }
}
