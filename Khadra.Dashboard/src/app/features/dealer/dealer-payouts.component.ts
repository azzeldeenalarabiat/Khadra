import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { PayableScope } from '../../core/services/admin-payables.service';
import { DealerConsoleService } from '../../core/services/dealer-console.service';
import { DealerPayoutsService } from '../../core/services/dealer-payouts.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import {
  PayoutFormat,
  PayoutWords,
  balanceRow,
  movedText,
  payableRow,
  settlementLineRow,
  settlementRow,
} from '../payouts/payouts.presenter';

/**
 * The office's payouts (payments Phase 8): what Khadra owes it — or it owes Khadra — per currency, each booking behind
 * that once its outcome is final, and the settlements Khadra recorded, each with the bookings it closed. Read only:
 * Khadra pays by transfer and records it; nothing here moves money. For the owner and an employee granted the reports
 * (spec 4.2 / 4.5); anyone else is told whose it is, from `GET /dealers/me`, never from a refusal.
 */
@Component({
  selector: 'kh-dealer-payouts',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dealer-payouts.component.html',
  imports: [RouterLink, IconComponent],
})
export class DealerPayoutsComponent {
  private readonly i18n = inject(I18nService);
  protected readonly t = this.i18n.t;
  private readonly formats = inject(FormatService);
  private readonly service = inject(DealerPayoutsService);
  private readonly console = inject(DealerConsoleService);

  /** Whether this member of staff holds the grant: `null` until `GET /dealers/me` answers. */
  protected readonly granted = computed(() => this.console.permissions()?.canViewReports ?? null);

  constructor() {
    // Nothing is asked for without the grant: a request that is not sent cannot answer 403.
    effect(() => this.service.show(this.granted() === true));
    inject(DestroyRef).onDestroy(() => {
      this.service.show(false);
      this.service.viewingSettlement.set(null);
    });
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
  protected readonly payablesResource = this.service.payables;
  private readonly balances = loaded(this.balancesResource);
  private readonly payables = loaded(this.payablesResource);
  private readonly settlements = loaded(this.service.settlements);
  private readonly settlement = loaded(this.service.settlement);

  protected readonly scope = this.service.scope;
  protected readonly scopes: readonly PayableScope[] = ['open', 'settled', 'all'];
  protected readonly payablesPage = this.service.payablesPage;
  protected readonly settlementsPage = this.service.settlementsPage;
  protected readonly viewing = this.service.viewingSettlement;

  protected readonly balanceRows = computed(() => (this.balances()?.balances ?? []).map((balance) => balanceRow(balance, 'office', this.words(), this.format)));
  protected readonly payableRows = computed(() => (this.payables()?.items ?? []).map((payable) => payableRow(payable, 'office', this.words(), this.format)));
  protected readonly payablePages = computed(() => this.payables()?.totalPages ?? 1);
  protected readonly settlementRows = computed(() => (this.settlements()?.items ?? []).map((settlement) => settlementRow(settlement, 'office', this.words(), this.format)));
  protected readonly settlementPages = computed(() => this.settlements()?.totalPages ?? 1);
  protected readonly opened = computed(() => {
    const detail = this.viewing() ? this.settlement() : null;
    if (!detail) return null;
    return {
      settlement: settlementRow(detail.settlement, 'office', this.words(), this.format),
      movement: movedText(detail.settlement.amount, this.words(), this.format),
      lines: detail.lines.map((line) => settlementLineRow(line, 'office', this.words(), this.format)),
    };
  });

  protected readonly failed = computed(() => !!this.balancesResource.error() || !!this.payablesResource.error());

  protected setScope(scope: PayableScope): void {
    this.scope.set(scope);
    this.payablesPage.set(1);
  }

  protected goToPayables(page: number): void {
    this.payablesPage.set(Math.max(1, page));
  }

  protected goToSettlements(page: number): void {
    this.settlementsPage.set(Math.max(1, page));
  }

  protected open(settlementId: string): void {
    this.viewing.set(this.viewing() === settlementId ? null : settlementId);
  }

  protected reload(): void {
    this.balancesResource.reload();
    this.payablesResource.reload();
    this.service.settlements.reload();
  }
}
