import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { ReportPeriod } from '../../core/models/dealer-console.api';
import { DealerConsoleService } from '../../core/services/dealer-console.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';

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
  imports: [IconComponent],
})
export class DealerReportsComponent {
  private readonly service = inject(DealerConsoleService);

  protected readonly periods: readonly { key: ReportPeriod; label: string }[] = [
    { key: 'daily', label: 'Today' },
    { key: 'weekly', label: 'This week' },
    { key: 'monthly', label: 'This month' },
  ];

  protected readonly period = this.service.period;
  protected readonly resource = this.service.report;
  /** Guarded: `value()` throws in the error state, so nothing reads the resource directly. */
  private readonly data = loaded(this.resource);
  protected readonly report = computed(() => this.data() ?? null);

  protected readonly failure = computed(() => {
    const error = this.resource.error() as
      { status?: number; error?: { code?: string } } | undefined;
    if (!error) return null;
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
