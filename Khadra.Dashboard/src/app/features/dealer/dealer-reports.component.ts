import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ReportPeriod } from '../../core/models/dealer-console.api';
import { DealerConsoleService } from '../../core/services/dealer-console.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { I18nService } from '../../core/i18n/i18n.service';

/**
 * Reports (spec 4.5, design `isReports`).
 *
 * Every figure is computed from bookings at the rates FROZEN on each one; nothing here is
 * recalculated from a current setting. Revenue counts cars that came back in the period; what is
 * still out is shown separately as "in progress". Net payout is not shown because payouts are not
 * built -- the card says so instead of showing a number that nothing will ever pay.
 */
@Component({
  selector: 'kh-dealer-reports',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dealer-reports.component.html',
  imports: [IconComponent, RouterLink],
})
export class DealerReportsComponent {
  protected readonly t = inject(I18nService).t;
  private readonly service = inject(DealerConsoleService);

  protected readonly periods: readonly { key: ReportPeriod; label: string }[] = [
    { key: 'daily', label: 'Today' },
    { key: 'weekly', label: this.t('dealerReports.thisWeek') },
    { key: 'monthly', label: this.t('dealerReports.thisMonth') },
  ];

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
      return 'Reports are for the dealer owner and staff they have granted access to. Ask the owner if you need them.';
    }
    return 'Reports could not be loaded. Nothing has been changed.';
  });

  protected readonly range = computed(() => {
    const r = this.report();
    if (!r) return '';
    const from = new Date(r.from + 'T00:00:00');
    const to = new Date(r.to + 'T00:00:00');
    const fmt = (d: Date) => d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short' });
    return r.from === r.to ? fmt(from) : `${fmt(from)} – ${fmt(to)}`;
  });

  protected select(period: ReportPeriod): void {
    this.period.set(period);
  }
}
