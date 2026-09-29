import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { serverSentence, snapshotProblem } from '../../core/i18n/problem';
import { AdminPayablesService } from '../../core/services/admin-payables.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { PayoutFormat, PayoutWords, balanceRow, holdRow } from './payouts.presenter';

/**
 * Payouts (payments Phase 8): every office's balance — what Khadra owes it or it owes Khadra, due now, and what is
 * recorded but held back or blocked — and the bookings a hold stops being recorded at all. Each office opens its own
 * page, where its payables are read and a settlement is recorded. Nothing here moves money: there is no payout rail,
 * and a settlement records a transfer an administrator made by hand.
 */
@Component({
  selector: 'kh-payouts',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './payouts.component.html',
  imports: [RouterLink, IconComponent],
})
export class PayoutsComponent {
  private readonly i18n = inject(I18nService);
  protected readonly t = this.i18n.t;
  private readonly formats = inject(FormatService);
  private readonly service = inject(AdminPayablesService);
  private readonly router = inject(Router);

  constructor() {
    this.service.show('payouts');
    inject(DestroyRef).onDestroy(() => this.service.show(null));
  }

  private readonly format: PayoutFormat = {
    money: (value) => this.formats.money(value.amount, value.currency),
    dateTime: (iso) => this.formats.dateTime(iso),
    day: (isoDay) => this.formats.calendarDay(isoDay),
  };

  private readonly words = computed<PayoutWords>(() => {
    this.i18n.lang();
    return { t: this.t, label: this.i18n.enumLabel };
  });

  protected readonly balancesResource = this.service.balances;
  protected readonly holdsResource = this.service.bookingHolds;
  private readonly balances = loaded(this.balancesResource);
  private readonly holds = loaded(this.holdsResource);

  protected readonly rows = computed(() => (this.balances() ?? []).map((balance) => balanceRow(balance, 'admin', this.words(), this.format)));
  protected readonly holdRows = computed(() => (this.holds() ?? []).map((hold) => holdRow(hold, this.words(), this.format)));

  protected readonly failure = computed(() => {
    const error = this.balancesResource.error();
    if (!error) return null;
    return serverSentence(snapshotProblem(error), this.i18n.lang(), this.t) ?? this.t('payouts.loadFailed');
  });

  protected open(dealerId: string): void {
    void this.router.navigate(['/payouts', dealerId]);
  }

  protected reload(): void {
    this.balancesResource.reload();
    this.holdsResource.reload();
  }
}
