import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { problemMessage, serverSentence, snapshotProblem } from '../../core/i18n/problem';
import { AdminPayablesService } from '../../core/services/admin-payables.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { PayoutFormat, PayoutWords, netText, settlementLineRow, settlementRow } from './payouts.presenter';

/**
 * One settlement (payments Phase 8): what moved, which way, on which day, under which reference, who recorded it, and
 * the payables it closed at their nets — and voiding it when it was recorded wrongly, which opens those payables
 * again. A voided settlement stays readable, marked void, with who voided it and why.
 */
@Component({
  selector: 'kh-office-settlement',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './office-settlement.component.html',
  imports: [RouterLink, IconComponent],
})
export class OfficeSettlementComponent {
  private readonly i18n = inject(I18nService);
  protected readonly t = this.i18n.t;
  private readonly formats = inject(FormatService);
  private readonly service = inject(AdminPayablesService);
  private readonly ui = inject(ConsoleUiService);
  private readonly route = inject(ActivatedRoute);

  private readonly settlementId = toSignal(this.route.paramMap.pipe(map((params) => params.get('settlementId'))), {
    initialValue: this.route.snapshot.paramMap.get('settlementId'),
  });

  constructor() {
    effect(() => this.service.viewingSettlement.set(this.settlementId()));
    inject(DestroyRef).onDestroy(() => this.service.viewingSettlement.set(null));
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

  protected readonly resource = this.service.settlement;
  private readonly detail = loaded(this.resource);

  protected readonly page = computed(() => {
    const detail = this.detail();
    if (!detail) return null;
    const words = this.words();
    return {
      settlement: settlementRow(detail.settlement, 'admin', words, this.format),
      movement: netText(detail.settlement.amount, 'admin', words, this.format),
      lines: detail.lines.map((line) => settlementLineRow(line, 'admin', words, this.format)),
    };
  });

  protected readonly failure = computed(() => {
    const error = this.resource.error();
    if (!error) return null;
    return serverSentence(snapshotProblem(error), this.i18n.lang(), this.t) ?? this.t('payouts.settlementLoadFailed');
  });

  protected reload(): void {
    this.resource.reload();
  }

  /**
   * Voids this settlement. The dialog states the consequence first: its payables are open and due again, and nothing
   * is deleted. A refusal that asking again cannot mend — voided already — reloads the page.
   */
  protected voidSettlement(): void {
    const page = this.page();
    if (!page || page.settlement.voided) return;
    const { settlement } = page;
    this.ui.openAction(
      {
        icon: 'file-x',
        tone: 'bad',
        danger: true,
        title: this.t('payouts.void.title', { number: settlement.number }),
        body: this.t('payouts.void.body', { count: settlement.count }),
        note: this.t('payouts.void.note'),
        fields: [{ name: 'reason', label: this.t('payouts.void.reason'), type: 'text' }],
        confirm: this.t('payouts.void.confirm'),
        result: { title: this.t('payouts.void.done'), body: '', tone: 'ok' },
      },
      async (values) => {
        try {
          await this.service.voidSettlement(settlement.id, values['reason'] ?? '');
          this.resource.reload();
          return { title: this.t('payouts.void.done'), body: this.t('payouts.void.doneBody', { number: settlement.number }), tone: 'ok' };
        } catch (error) {
          const problem = snapshotProblem(error);
          if (problem.code !== 'payables.settlement_already_voided') throw error;
          this.resource.reload();
          return {
            title: this.t('common.thatDidNotGoThrough'),
            body: problemMessage(problem, this.i18n.lang(), this.t) ?? this.t('common.serviceDidNotRespond'),
            tone: 'bad',
          };
        }
      },
      { title: this.t('payouts.void.done'), body: '' },
    );
  }
}
