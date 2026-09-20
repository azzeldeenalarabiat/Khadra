import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ReportPeriod } from '../../core/models/dealer-console.api';
import { DealerConsoleService } from '../../core/services/dealer-console.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { TranslationKey } from '../../core/i18n/en';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { MoneyPipe } from '../../shared/money.pipe';

/** The windows a report can cover, as keys: the tabs are worded when they render. */
const PERIODS: readonly { readonly key: ReportPeriod; readonly label: TranslationKey }[] = [
  { key: 'daily', label: 'dealerReports.today' },
  { key: 'weekly', label: 'dealerReports.thisWeek' },
  { key: 'monthly', label: 'dealerReports.thisMonth' },
];

/**
 * Reports (spec 4.5, design `isReports`).
 *
 * Every figure is computed from bookings at the rates FROZEN on each one; nothing here is
 * recalculated from a current setting. Revenue counts cars that came back in the period; what is
 * still out is shown separately as "in progress". Net payout is not shown because payouts are not
 * built -- the card says so instead of showing a number that nothing will ever pay.
 *
 * Platform commission is the amount Khadra charges, shown unsigned. The server sends it as a
 * non-negative amount and computes "revenue after commission" itself, so the labels carry the
 * subtraction and the screen never performs it.
 */
@Component({
  selector: 'kh-dealer-reports',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dealer-reports.component.html',
  imports: [IconComponent, RouterLink, MoneyPipe],
})
export class DealerReportsComponent {
  protected readonly t = inject(I18nService).t;
  protected readonly formats = inject(FormatService);
  private readonly service = inject(DealerConsoleService);

  /**
   * The period tabs, worded in the reader's language.
   *
   * A `computed` rather than a field: a field initialiser resolves `t()` once, at construction, so the
   * tabs stayed in whichever language the screen opened in — and "Today" had never been keyed at all.
   */
  protected readonly periods = computed(() =>
    PERIODS.map((period) => ({ key: period.key, label: this.t(period.label) })),
  );

  protected readonly period = this.service.period;
  protected readonly resource = this.service.report;
  /** Guarded: `value()` throws in the error state, so nothing reads the resource directly. */
  private readonly data = loaded(this.resource);
  protected readonly report = computed(() => this.data() ?? null);

  /**
   * Whether this member of staff holds the grant, from `GET /dealers/me` rather than from a refusal.
   *
   * The request is not sent without it, so waiting for a 403 to arrive would be waiting forever —
   * and the template's last branch is a loading skeleton, which is what "forever" would have looked
   * like. `null` while the answer is unknown, so neither block renders before there is something
   * true to say.
   */
  protected readonly granted = computed(() => this.service.permissions()?.canViewReports ?? null);

  protected readonly failure = computed(() => {
    const error = this.resource.error() as
      { status?: number; error?: { code?: string } } | undefined;
    if (!error) return null;
    // Still reachable, and worth keeping: the owner can withdraw the grant while the screen is
    // open, and the next period the reader clicks answers 403 before `me` has caught up.
    if (error.error?.code === 'dealer.reports_not_granted' || error.status === 403) {
      return this.t('dealerReports.reportsAreForThe');
    }
    return this.t('dealerReports.reportsCouldNotBe');
  });

  /**
   * "01 Sept – 30 Sept", from the report's own calendar dates.
   *
   * `from` and `to` are dates in the reporting calendar, not instants, so they are formatted without
   * passing through the browser's zone — which could have moved either one to a neighbouring day.
   */
  protected readonly range = computed(() => {
    const r = this.report();
    if (!r) return '';
    const from = this.formats.calendarDayMonth(r.from);
    return r.from === r.to ? from : `${from} – ${this.formats.calendarDayMonth(r.to)}`;
  });

  protected select(period: ReportPeriod): void {
    this.period.set(period);
  }
}
