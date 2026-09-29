import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { serverSentence, snapshotProblem } from '../../core/i18n/problem';
import { AdminPayablesService } from '../../core/services/admin-payables.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { PayoutFormat, PayoutWords, financeGroup, payableRow } from '../payouts/payouts.presenter';

/**
 * Finance (payments Phase 8): Khadra's own money over a span of Amman days — commission earned on the outcomes that
 * became final, what disputes left with the platform (beside commission, never inside it), what moved to and from
 * offices by the day it moved, and what is owed either way right now — then the commission report, every payable of
 * the span. Every figure is a sum the server made of recorded rows; the current month unless another span is asked.
 */
@Component({
  selector: 'kh-finance',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './finance.component.html',
  imports: [RouterLink, IconComponent],
})
export class FinanceComponent {
  private readonly i18n = inject(I18nService);
  protected readonly t = this.i18n.t;
  private readonly formats = inject(FormatService);
  private readonly service = inject(AdminPayablesService);

  constructor() {
    this.service.show('finance');
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

  protected readonly resource = this.service.finance;
  protected readonly reportResource = this.service.report;
  private readonly summary = loaded(this.resource);
  private readonly report = loaded(this.reportResource);

  /** The span being typed, before it is asked for. */
  protected readonly from = signal(this.service.financeRange().from);
  protected readonly to = signal(this.service.financeRange().to);

  protected readonly span = computed(() => {
    const summary = this.summary();
    return summary ? this.t('finance.span', { from: this.format.day(summary.from), to: this.format.day(summary.to) }) : null;
  });
  protected readonly groups = computed(() => (this.summary()?.totals ?? []).map((totals) => financeGroup(totals, this.words(), this.format)));
  protected readonly attention = computed(() => {
    const summary = this.summary();
    return summary && (summary.held > 0 || summary.blockedPayables > 0)
      ? this.t('finance.attention', { held: summary.held, blocked: summary.blockedPayables })
      : null;
  });
  protected readonly rows = computed(() => (this.report()?.items ?? []).map((payable) => payableRow(payable, 'admin', this.words(), this.format)));
  protected readonly page = this.service.reportPage;
  protected readonly pages = computed(() => this.report()?.totalPages ?? 1);

  protected readonly failure = computed(() => {
    const error = this.resource.error();
    if (!error) return null;
    return serverSentence(snapshotProblem(error), this.i18n.lang(), this.t) ?? this.t('finance.loadFailed');
  });

  protected setFrom(event: Event): void {
    this.from.set((event.target as HTMLInputElement).value);
  }

  protected setTo(event: Event): void {
    this.to.set((event.target as HTMLInputElement).value);
  }

  protected apply(): void {
    this.service.setFinanceRange(this.from(), this.to());
  }

  protected thisMonth(): void {
    this.from.set('');
    this.to.set('');
    this.service.setFinanceRange('', '');
  }

  protected goTo(page: number): void {
    this.page.set(Math.max(1, page));
  }

  protected reload(): void {
    this.resource.reload();
  }
}
