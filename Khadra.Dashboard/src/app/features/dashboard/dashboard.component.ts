import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { AdminDashboardService } from '../../core/services/admin-dashboard.service';
import {
  formatChangePercent,
  toActivityRows,
  toKpiCards,
  toMoneyRows,
  toQueueItems,
  toTrendHeights,
} from '../../core/services/dashboard.presenter';
import { toneClass } from '../../core/models/console.models';
import { IconComponent } from '../../shared/icon/icon.component';

/**
 * Landing screen: platform figures, the work queue that drives the admin SLA, and a short activity
 * feed. All of it from `GET /api/v1/admin/dashboard`.
 *
 * The clock ticks locally. The API sends absolute instants rather than "13h over", so the countdowns
 * and the SLA meters stay honest between refreshes without asking the server again.
 */
@Component({
  selector: 'kh-dashboard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dashboard.component.html',
  imports: [RouterLink, IconComponent],
})
export class DashboardComponent {
  private readonly service = inject(AdminDashboardService);
  private readonly now = signal(Date.now());

  protected readonly resource = this.service.dashboard;
  protected readonly toneClass = toneClass;

  constructor() {
    const ticker = setInterval(() => this.now.set(Date.now()), 30_000);
    inject(DestroyRef).onDestroy(() => clearInterval(ticker));
  }

  protected readonly kpis = computed(() => {
    const data = this.resource.value();
    return data ? toKpiCards(data) : [];
  });

  protected readonly queue = computed(() => {
    const data = this.resource.value();
    return data ? toQueueItems(data.attentionQueue, this.now()) : [];
  });

  protected readonly trend = computed(() => {
    const data = this.resource.value();
    return data ? toTrendHeights(data.bookingTrend) : [];
  });

  protected readonly money = computed(() => {
    const data = this.resource.value();
    return data ? toMoneyRows(data) : [];
  });

  protected readonly activity = computed(() => {
    const data = this.resource.value();
    return data ? toActivityRows(data.recentActivity, this.now()) : [];
  });

  protected readonly trendChange = computed(() =>
    formatChangePercent(this.resource.value()?.bookingTrend.changePercent ?? null),
  );

  protected readonly trendDays = computed(
    () => this.resource.value()?.bookingTrend.points.length ?? 0,
  );

  protected readonly queueSummary = computed(() => {
    const queue = this.resource.value()?.attentionQueue;
    return queue ? `${queue.openCount} open · ${queue.overdueCount} overdue` : '';
  });

  protected readonly slaNote = computed(() => {
    const hours = this.resource.value()?.adminSlaHours;
    return hours ? `${hours}-hour SLA` : '';
  });

  /**
   * A 401 or 403 is not a broken dashboard, it is a missing session, and telling an admin to "try
   * again" when they simply are not signed in wastes their time.
   */
  protected readonly failure = computed(() => {
    const error = this.resource.error() as { status?: number } | undefined;
    if (!error) return null;

    const status = error.status ?? 0;
    if (status === 401) {
      return { title: 'Your session has expired', body: 'Sign in again to see platform figures.' };
    }
    if (status === 403) {
      return {
        title: 'This account cannot see the platform dashboard',
        body: 'Platform figures are restricted to administrators.',
      };
    }
    return {
      title: 'The dashboard could not be loaded',
      body: 'The platform figures service did not respond. Nothing has been changed.',
    };
  });

  protected reload(): void {
    this.service.reload();
  }
}
