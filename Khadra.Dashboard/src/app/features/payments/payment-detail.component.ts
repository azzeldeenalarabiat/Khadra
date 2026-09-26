import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { serverSentence, snapshotProblem } from '../../core/i18n/problem';
import { AdminPaymentsService } from '../../core/services/admin-payments.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { paymentPage } from './payment-detail.presenter';

/**
 * One payment (payments Phase 4b): the attempt as its booking's financial state describes it, its
 * refunds with booking money and fee apart, the booking it belongs to, and every event the provider
 * sent about it — including one that arrived before the payment's reference was saved, which only its
 * reference ties to it. Read-only; the live checkout link is never on this page.
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
}
