import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { problemMessage, serverSentence, snapshotProblem } from '../../core/i18n/problem';
import { AdminPayablesService, PayableScope } from '../../core/services/admin-payables.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import {
  BalanceRow,
  PayableRow,
  PayoutFormat,
  PayoutWords,
  balanceRow,
  currentAmountOf,
  holdRow,
  netText,
  payableRow,
  settlementRow,
} from './payouts.presenter';

/** Refusals after which asking again cannot help as it stands: the page reloads to show what changed. */
const RELOADING_REFUSALS: ReadonlySet<string> = new Set([
  'payables.nothing_due',
  'payables.records_changed',
  'payables.changed_concurrently',
  'payables.already_held',
  'payables.not_held',
  'payables.already_settled',
]);

/**
 * One office's payouts (payments Phase 8): its balance per currency and kind of money, the payables behind it —
 * open, settled or all — with the holds and blocks that keep one from being due, the bookings a hold stops being
 * recorded, and its settlements. The actions: record a settlement of the whole balance due, netted, and hold or release
 * one payable. Every one states its consequence first and is audited.
 */
@Component({
  selector: 'kh-office-payouts',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './office-payouts.component.html',
  imports: [RouterLink, IconComponent],
})
export class OfficePayoutsComponent {
  private readonly i18n = inject(I18nService);
  protected readonly t = this.i18n.t;
  private readonly formats = inject(FormatService);
  private readonly service = inject(AdminPayablesService);
  private readonly ui = inject(ConsoleUiService);
  private readonly route = inject(ActivatedRoute);

  private readonly dealerId = toSignal(this.route.paramMap.pipe(map((params) => params.get('dealerId'))), {
    initialValue: this.route.snapshot.paramMap.get('dealerId'),
  });

  constructor() {
    effect(() => {
      this.service.office.set(this.dealerId());
      this.service.payablesPage.set(1);
      this.service.settlementsPage.set(1);
    });
    inject(DestroyRef).onDestroy(() => this.service.office.set(null));
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

  protected readonly balancesResource = this.service.officeBalances;
  protected readonly payablesResource = this.service.officePayables;
  protected readonly settlementsResource = this.service.officeSettlements;
  private readonly balances = loaded(this.balancesResource);
  private readonly payables = loaded(this.payablesResource);
  private readonly settlements = loaded(this.settlementsResource);
  private readonly holds = loaded(this.service.officeHolds);

  protected readonly scope = this.service.scope;
  protected readonly scopes: readonly PayableScope[] = ['open', 'nothingDue', 'settled', 'all'];
  protected readonly payablesPage = this.service.payablesPage;
  protected readonly settlementsPage = this.service.settlementsPage;

  protected readonly office = computed(() => this.balances()?.[0]?.dealerName ?? this.payables()?.items[0]?.dealerName ?? null);
  protected readonly balanceRows = computed(() => (this.balances() ?? []).map((balance) => balanceRow(balance, 'admin', this.words(), this.format)));
  protected readonly payableRows = computed(() => (this.payables()?.items ?? []).map((payable) => payableRow(payable, 'admin', this.words(), this.format)));
  protected readonly payablePages = computed(() => this.payables()?.totalPages ?? 1);
  protected readonly settlementRows = computed(() => (this.settlements()?.items ?? []).map((settlement) => settlementRow(settlement, 'admin', this.words(), this.format)));
  protected readonly settlementPages = computed(() => this.settlements()?.totalPages ?? 1);
  protected readonly holdRows = computed(() => (this.holds() ?? []).map((hold) => holdRow(hold, this.words(), this.format)));

  protected readonly failure = computed(() => {
    const error = this.balancesResource.error() ?? this.payablesResource.error();
    if (!error) return null;
    return serverSentence(snapshotProblem(error), this.i18n.lang(), this.t) ?? this.t('payouts.loadFailed');
  });

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

  protected reload(): void {
    this.service.reloadOffice();
  }

  /**
   * Records that the whole balance due in this currency and kind of money moved by hand. The dialog states what it
   * records — who paid whom, how much, over how many bookings — before anything happens; the day it moved defaults
   * to today and can be no later. A balance that changed meanwhile is refused with the balance due now, and the page
   * reloads so the next attempt confirms the figure on screen.
   */
  protected recordSettlement(row: BalanceRow): void {
    const dealerId = this.dealerId();
    if (!dealerId || !row.canSettle || !row.provider) return;
    const today = this.formats.todayIso();
    const title = this.t(
      row.dueAmount > 0 ? 'payouts.record.titlePayout' : row.dueAmount < 0 ? 'payouts.record.titleReceived' : 'payouts.record.titleNetted',
    );
    this.ui.openAction(
      {
        icon: 'arrow-line-up-right',
        tone: 'accent',
        title,
        body: this.t('payouts.record.body', { office: row.office, balance: row.due, count: row.dueCount }),
        note: row.isTest ? this.t('payouts.record.testNote') : this.t('payouts.record.note'),
        fields: [
          { name: 'paidOn', label: this.t('payouts.record.paidOn'), type: 'date', value: today, max: today },
          { name: 'reference', label: this.t('payouts.record.reference'), type: 'line', optional: true },
          { name: 'note', label: this.t('payouts.record.noteLabel'), type: 'text', optional: true },
        ],
        confirm: this.t('payouts.record.confirm'),
        result: { title: this.t('payouts.record.done'), body: '', tone: 'ok' },
      },
      async (values) => {
        try {
          const recorded = await this.service.recordSettlement(dealerId, {
            currency: row.currency,
            provider: row.provider!,
            expectedAmount: row.dueAmount,
            paidOn: values['paidOn'] ?? today,
            reference: values['reference'] || null,
            note: values['note'] || null,
          });
          this.service.reloadOffice();
          return {
            title: this.t('payouts.record.done'),
            body: this.t('payouts.record.doneBody', { number: recorded.settlement.number }),
            tone: 'ok',
          };
        } catch (error) {
          const problem = snapshotProblem(error);
          if (problem.code === 'payables.balance_changed') {
            this.service.reloadOffice();
            const current = currentAmountOf(error);
            return {
              title: this.t('common.thatDidNotGoThrough'),
              body: current
                ? this.t('payouts.record.balanceChanged', { balance: netText(current, 'admin', this.words(), this.format).text })
                : (problemMessage(problem, this.i18n.lang(), this.t) ?? this.t('common.serviceDidNotRespond')),
              tone: 'bad',
            };
          }
          if (!problem.code || !RELOADING_REFUSALS.has(problem.code)) throw error;
          this.service.reloadOffice();
          return {
            title: this.t('common.thatDidNotGoThrough'),
            body: problemMessage(problem, this.i18n.lang(), this.t) ?? this.t('common.serviceDidNotRespond'),
            tone: 'bad',
          };
        }
      },
      { title: this.t('payouts.record.done'), body: '' },
    );
  }

  /** Leaves one payable out of settlements, with the administrator's reason. */
  protected hold(row: PayableRow): void {
    if (!row.canHold) return;
    this.ui.openAction(
      {
        icon: 'pause-circle',
        tone: 'warn',
        title: this.t('payouts.hold.title', { reference: row.reference }),
        body: this.t('payouts.hold.body'),
        fields: [{ name: 'reason', label: this.t('payouts.hold.reason'), type: 'text' }],
        confirm: this.t('payouts.hold.confirm'),
        result: { title: this.t('payouts.hold.done'), body: '', tone: 'ok' },
      },
      async (values) => this.act(() => this.service.hold(row.id, values['reason'] ?? ''), this.t('payouts.hold.done')),
      { title: this.t('payouts.hold.done'), body: '' },
    );
  }

  /** Lets a payable the administrator held back into the next settlement. */
  protected release(row: PayableRow): void {
    if (!row.canRelease) return;
    this.ui.openAction(
      {
        icon: 'check-circle',
        tone: 'accent',
        title: this.t('payouts.release.title', { reference: row.reference }),
        body: this.t('payouts.release.body'),
        fields: [{ name: 'note', label: this.t('payouts.release.note'), type: 'text', optional: true }],
        confirm: this.t('payouts.release.confirm'),
        result: { title: this.t('payouts.release.done'), body: '', tone: 'ok' },
      },
      async (values) => this.act(() => this.service.release(row.id, values['note'] || null), this.t('payouts.release.done')),
      { title: this.t('payouts.release.done'), body: '' },
    );
  }

  private async act(work: () => Promise<unknown>, done: string) {
    try {
      await work();
      this.service.reloadOffice();
      return { title: done, body: '', tone: 'ok' as const };
    } catch (error) {
      const problem = snapshotProblem(error);
      if (!problem.code || !RELOADING_REFUSALS.has(problem.code)) throw error;
      this.service.reloadOffice();
      return {
        title: this.t('common.thatDidNotGoThrough'),
        body: problemMessage(problem, this.i18n.lang(), this.t) ?? this.t('common.serviceDidNotRespond'),
        tone: 'bad' as const,
      };
    }
  }
}
