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
  toQueueItems,
  busiestDay,
  toTrendBars,
} from '../../core/services/dashboard.presenter';
import { Tone, toneClass } from '../../core/models/console.models';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';

/**
 * Landing screen: platform figures, the work queue that drives the admin SLA, and a short activity
 * feed. Each panel is its own request, so each has its own skeleton, its own failure and its own
 * retry — one slow or broken reader no longer blanks the screen.
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

  protected readonly dealers = this.service.dealerCounts;
  protected readonly bookings = this.service.bookingCounts;
  protected readonly customers = this.service.customerCounts;
  protected readonly disputes = this.service.disputeCounts;
  protected readonly queueResource = this.service.attentionQueue;
  protected readonly trendResource = this.service.bookingTrend;
  protected readonly activityResource = this.service.activity;

  // Every panel reads its data through loaded(): Resource.value() throws while a request has
  // failed, and one broken panel must not take the row -- or the shell around it -- down with it.
  private readonly dealerCounts = loaded(this.dealers);
  private readonly bookingCounts = loaded(this.bookings);
  private readonly customerCounts = loaded(this.customers);
  private readonly disputeCounts = loaded(this.disputes);
  private readonly queueData = loaded(this.queueResource);
  private readonly trendData = loaded(this.trendResource);
  private readonly activityData = loaded(this.activityResource);

  protected readonly toneClass = toneClass;

  constructor() {
    // Claims the panels for as long as this screen is mounted. They are root-scoped resources on a
    // service the sidebar injects, so without an owner they fetch on every admin screen; with one
    // they fetch here and nowhere else. Released automatically when the screen is destroyed.
    this.service.watch();
    // Entering the screen re-reads it: the resources outlive the component and would otherwise still
    // hold whatever they fetched the last time the dashboard was open.
    this.service.reload();

    const ticker = setInterval(() => this.now.set(Date.now()), 30_000);
    inject(DestroyRef).onDestroy(() => clearInterval(ticker));
  }

  /**
   * The KPI row, from four independent responses.
   *
   * A card appears as its own answer arrives rather than the row waiting for the slowest. There is no
   * Revenue card: the Payments context is not built, so nothing can answer for money, and the row
   * does not call an endpoint that could only ever reply "not built" — the money panel below says it
   * once, in words.
   */
  protected readonly kpis = computed(() =>
    toKpiCards({
      dealers: this.dealerCounts(),
      bookings: this.bookingCounts(),
      customers: this.customerCounts(),
      disputes: this.disputeCounts(),
    }),
  );

  protected readonly countsLoading = computed(
    () =>
      this.dealers.isLoading() ||
      this.bookings.isLoading() ||
      this.customers.isLoading() ||
      this.disputes.isLoading(),
  );

  protected readonly queue = computed(() => {
    const data = this.queueData();
    return data ? toQueueItems(data, this.now()) : [];
  });

  protected readonly trend = computed(() => {
    const data = this.trendData();
    return data ? toTrendBars(data) : [];
  });

  protected readonly activity = computed(() => {
    const data = this.activityData();
    return data ? toActivityRows(data.entries, this.now()) : [];
  });

  protected readonly trendChange = computed(() =>
    formatChangePercent(this.trendData()?.changePercent ?? null),
  );

  /**
   * The colour follows the number.
   *
   * This figure was painted green by a fixed `.trend-up` class, so a fortnight in which bookings
   * fell announced the fall in the colour the console uses for good news. A flat or unknown period
   * is neither, and reads as neither.
   */
  protected readonly trendTone = computed<Tone | null>(() => {
    const change = this.trendData()?.changePercent ?? null;
    if (change === null) return 'dim';
    if (change > 0) return 'ok';
    if (change < 0) return 'bad';
    return null;
  });

  protected readonly trendDays = computed(() => this.trendData()?.points.length ?? 0);

  /**
   * The number every bar is drawn as a proportion of, so the scale is stated rather than implied.
   * Null until the answer arrives — a 0 printed here would read as "no bookings in a fortnight".
   */
  protected readonly trendPeak = computed(() => {
    const data = this.trendData();
    return data ? busiestDay(data) : null;
  });

  /** The window the chart covers, taken from the response rather than worked out locally. */
  protected readonly trendRange = computed(() => {
    const data = this.trendData();
    if (!data) return '';
    return `${this.day(data.from)} – ${this.day(data.to)}`;
  });

  private day(iso: string): string {
    return new Date(iso + 'T00:00:00').toLocaleDateString('en-GB', {
      day: 'numeric',
      month: 'short',
    });
  }

  protected readonly queueSummary = computed(() => {
    const queue = this.queueData();
    return queue ? `${queue.openCount} open · ${queue.overdueCount} overdue` : '';
  });

  /** The SLA in force today, labelled as such — each row is judged against its own frozen window. */
  protected readonly slaNote = computed(() => {
    const hours = this.queueData()?.slaHours;
    return hours ? `Current SLA ${hours}h` : '';
  });

  /**
   * A 401 or 403 is not a broken dashboard, it is a missing session, and telling an admin to "try
   * again" when they simply are not signed in wastes their time.
   *
   * Read from the counts because they are the cheapest thing that proves the session: if the whole
   * screen is unauthorised, this is the page-level message rather than four identical panel errors.
   */
  protected readonly failure = computed(() => {
    const status = (this.dealers.error() as { status?: number } | undefined)?.status ?? 0;
    if (status === 401) {
      return { title: 'Your session has expired', body: 'Sign in again to see platform figures.' };
    }
    if (status === 403) {
      return {
        title: 'This account cannot see the platform dashboard',
        body: 'Platform figures are restricted to administrators.',
      };
    }
    return null;
  });

  /** Whether one panel failed on its own, while the rest of the screen is fine. */
  protected panelFailed(resource: { error: () => unknown }): boolean {
    return !!resource.error() && !this.failure();
  }

  /**
   * Every count is missing and none of them is still coming.
   *
   * A card is simply omitted until its own answer lands, which is right while one is in flight and
   * wrong once it has failed: four omitted cards leave an empty band where the platform's figures
   * belong, and an empty band reads as a platform with nothing on it rather than a question nobody
   * could answer.
   */
  protected readonly countsFailed = computed(
    () => this.kpis().length === 0 && !this.countsLoading() && !this.failure(),
  );

  protected reload(): void {
    this.service.reload();
  }
}
