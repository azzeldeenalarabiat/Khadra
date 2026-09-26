import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslationKey } from '../../core/i18n/en';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { Language } from '../../core/i18n/language';
import { ProblemSnapshot, serverSentence, snapshotProblem } from '../../core/i18n/problem';
import { AdminPaymentsService, NO_PAYMENT_FILTERS, PaymentFilters } from '../../core/services/admin-payments.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { ListFormat, paymentRow, refundRow, refundViewLabel } from './payments.presenter';

/**
 * Every checkout attempt on the platform, and every refund owed back (payments Phase 4b).
 *
 * Two tabs over one route family: `/payments` lists the attempts, newest first, filtered by the
 * domain's own words; `/payments/refunds` is the queue pre-launch item 157 asks for — refused refunds
 * first, each owed longest first. Read-only: the payment sweep sends every refund, a refused one again
 * by itself, so nothing here has a button that moves money.
 */
@Component({
  selector: 'kh-payments',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './payments.component.html',
  imports: [RouterLink, IconComponent],
})
export class PaymentsComponent {
  private readonly i18n = inject(I18nService);
  protected readonly t = this.i18n.t;
  private readonly enumLabel = this.i18n.enumLabel;
  private readonly formats = inject(FormatService);
  private readonly service = inject(AdminPaymentsService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  /** Which tab the route opened. */
  protected readonly view: 'payments' | 'refunds' =
    this.route.snapshot.data['view'] === 'refunds' ? 'refunds' : 'payments';

  constructor() {
    this.service.show(this.view);
    inject(DestroyRef).onDestroy(() => this.service.show(null));

    // A link from another screen arrives with its filter: the dashboard's captures being refunded, a
    // dealership's or a customer's payments. Anything the link does not name starts clear.
    const query = this.route.snapshot.queryParamMap;
    const linked = ['status', 'purpose', 'reference', 'dealerId', 'customerId'].some((key) => query.has(key));
    if (this.view === 'payments' && linked) {
      this.service.setFilters({
        ...NO_PAYMENT_FILTERS,
        status: query.get('status') ?? '',
        purpose: query.get('purpose') ?? '',
        reference: query.get('reference') ?? '',
        dealerId: query.get('dealerId') ?? '',
        customerId: query.get('customerId') ?? '',
      });
    }
  }

  private readonly format: ListFormat = {
    money: (value) => this.formats.money(value.amount, value.currency),
    when: (iso) => this.formats.dayMonthTime(iso),
  };

  protected readonly vocabulary = loaded(this.service.vocabulary);

  // ── Payments ──
  protected readonly filters = this.service.filters;
  protected readonly page = this.service.page;
  protected readonly paymentsResource = this.service.payments;
  private readonly paymentsPage = loaded(this.paymentsResource);
  protected readonly paymentRows = computed(() =>
    (this.paymentsPage()?.items ?? []).map((row) => paymentRow(row, this.t, this.enumLabel, this.format)),
  );
  protected readonly paymentsTotal = computed(() => this.paymentsPage()?.totalCount ?? 0);
  protected readonly paymentsPages = computed(() => this.paymentsPage()?.totalPages ?? 1);
  protected readonly filtered = computed(() => Object.values(this.filters()).some((value) => value.trim() !== ''));
  protected readonly statusOptions = computed(() =>
    (this.vocabulary()?.paymentStatuses ?? []).map((name) => ({ name, label: this.enumLabel('paymentStatus', name) })),
  );
  protected readonly purposeOptions = computed(() =>
    (this.vocabulary()?.paymentPurposes ?? []).map((name) => ({ name, label: this.enumLabel('paymentPurpose', name) })),
  );

  // ── Refunds ──
  protected readonly refundView = this.service.refundView;
  protected readonly refundPage = this.service.refundPage;
  protected readonly refundsResource = this.service.refunds;
  private readonly refundsPage = loaded(this.refundsResource);
  protected readonly refundRows = computed(() =>
    (this.refundsPage()?.items ?? []).map((row) => refundRow(row, this.t, this.enumLabel, this.format)),
  );
  protected readonly refundsTotal = computed(() => this.refundsPage()?.totalCount ?? 0);
  protected readonly refundsPages = computed(() => this.refundsPage()?.totalPages ?? 1);
  protected readonly refundViews = computed(() => [
    { key: 'live', label: refundViewLabel('live', this.t, this.enumLabel) },
    ...(this.vocabulary()?.refundStatuses ?? []).map((name) => ({
      key: name,
      label: refundViewLabel(name, this.t, this.enumLabel),
    })),
  ]);

  /** A failed load, held as facts and worded here, so a language switch re-words it. */
  protected readonly failure = computed(() => {
    const error = this.view === 'payments' ? this.paymentsResource.error() : this.refundsResource.error();
    if (!error) return null;
    return describe(
      snapshotProblem(error),
      this.t,
      this.i18n.lang(),
      this.view === 'payments' ? 'payments.loadFailed' : 'refunds.loadFailed',
    );
  });

  protected setFilter(key: keyof PaymentFilters, event: Event): void {
    const value = (event.target as HTMLInputElement | HTMLSelectElement).value;
    this.service.setFilters({ ...this.filters(), [key]: value });
  }

  protected clearFilters(): void {
    this.service.setFilters(NO_PAYMENT_FILTERS);
  }

  protected goToPayments(page: number): void {
    this.page.set(Math.max(1, page));
  }

  protected selectRefundView(view: string): void {
    this.service.setRefundView(view);
  }

  protected goToRefunds(page: number): void {
    this.refundPage.set(Math.max(1, page));
  }

  protected open(paymentId: string): void {
    void this.router.navigate(['/payments', paymentId]);
  }

  protected reload(): void {
    if (this.view === 'payments') this.paymentsResource.reload();
    else this.refundsResource.reload();
  }
}

/** Why a list could not load, in the language on screen when it is shown. */
function describe(
  problem: ProblemSnapshot,
  t: (key: TranslationKey) => string,
  language: Language,
  fallback: TranslationKey,
): string {
  return serverSentence(problem, language, t) ?? t(fallback);
}
