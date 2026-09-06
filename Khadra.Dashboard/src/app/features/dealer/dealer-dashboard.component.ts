import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Tone } from '../../core/models/console.models';
import { DealerDashboard, UpcomingHandover } from '../../core/models/dealer-console.api';
import { DealerBookingsService } from '../../core/services/dealer-bookings.service';
import { DealerConsoleService } from '../../core/services/dealer-console.service';
import { SessionService } from '../../core/services/session.service';
import { IconName } from '../../shared/icon/icon-paths';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { I18nService } from '../../core/i18n/i18n.service';

interface Kpi {
  readonly label: string;
  readonly main: string;
  readonly note: string;
  readonly icon: IconName;
  /**
   * Null where the tile has nowhere to send this reader.
   *
   * A withheld figure links to Reports, which is exactly the screen the person it was withheld from
   * cannot open — a tile that says "not granted" and then offers to show you it anyway.
   */
  readonly route: string | null;
  readonly query?: Record<string, string>;
}

interface Attention {
  readonly type: string;
  readonly title: string;
  readonly desc: string;
  readonly entity: string;
  readonly when: string;
  readonly status: string;
  readonly tone: Tone;
  readonly action: string;
  readonly route: string;
  readonly query?: Record<string, string>;
}

/**
 * The dealer's dashboard (design: Dealer Console, `isDashboard`).
 *
 * Every figure is computed server-side from the dealership's own bookings and fleet. "Available" is
 * derived from live bookings, never from a stored status; revenue and occupancy come back null for
 * staff without the reports grant and the tiles say so rather than showing a zero.
 */
@Component({
  selector: 'kh-dealer-dashboard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dealer-dashboard.component.html',
  imports: [RouterLink, IconComponent],
})
export class DealerDashboardComponent {
  protected readonly t = inject(I18nService).t;
  private readonly console = inject(DealerConsoleService);
  private readonly bookings = inject(DealerBookingsService);
  private readonly session = inject(SessionService);

  protected readonly resource = this.console.dashboard;
  /** Guarded: `value()` throws in the error state, so nothing reads the resource directly. */
  private readonly data = loaded(this.resource);
  protected readonly dashboard = computed(() => this.data() ?? null);

  /** Both "add a car" entry points on this screen lead into an owner-only form. */
  protected readonly canManageFleet = computed(
    () => this.console.permissions()?.canManageFleet === true,
  );

  protected readonly greeting = computed(() => {
    const hour = new Date().getHours();
    const part = hour < 12 ? 'Good morning' : hour < 18 ? 'Good afternoon' : 'Good evening';
    return `${part}, ${this.dashboard()?.businessName ?? this.session.user()?.fullName ?? ''}`;
  });

  protected readonly statusLine = computed(() => {
    const d = this.dashboard();
    if (!d) return '';
    const parts = [
      `${d.bookings.requested} booking ${d.bookings.requested === 1 ? 'request' : 'requests'} waiting`,
      `${d.upcomingPickups.length} pickups and ${d.upcomingReturns.length} returns in the next ${d.upcomingWindowHours} hours`,
    ];
    return parts.join(', ') + '.';
  });

  protected readonly kpis = computed<readonly Kpi[]>(() => {
    const d = this.dashboard();
    if (!d) return [];
    const oldest = d.bookings.oldestRequestedAt
      ? `oldest ${this.ago(d.bookings.oldestRequestedAt)}`
      : 'nothing waiting';
    return [
      {
        label: this.t('dealerDashboard.pendingRequests'),
        main: String(d.bookings.requested),
        note: `${oldest} · answer before pickup`,
        icon: 'bell-ringing',
        route: '/dealer/bookings',
        query: { tab: 'pending' },
      },
      {
        label: this.t('dealerDashboard.activeRentals'),
        main: String(d.bookings.pickedUp),
        note: d.bookings.overdueReturns
          ? `${d.bookings.overdueReturns} overdue`
          : 'all within their dates',
        icon: 'car-profile',
        route: '/dealer/bookings',
        query: { tab: 'active' },
      },
      {
        label: this.t('dealerDashboard.availableVehicles'),
        main: String(d.availableVehicles),
        note: `of ${d.publishedVehicles} published · ${d.totalVehicles} in your fleet`,
        icon: 'check-square',
        route: '/dealer/fleet',
      },
      {
        label: this.t('dealerDashboard.upcomingPickups'),
        main: String(d.upcomingPickups.length),
        note: d.upcomingPickups[0]
          ? `next: ${this.when(d.upcomingPickups[0].when)}`
          : `none in ${d.upcomingWindowHours}h`,
        icon: 'arrow-square-out',
        route: '/dealer/bookings',
        query: { tab: 'upcoming' },
      },
      {
        label: this.t('dealerDashboard.upcomingReturns'),
        main: String(d.upcomingReturns.length),
        note: d.upcomingReturns[0]
          ? `next: ${this.when(d.upcomingReturns[0].when)}`
          : `none in ${d.upcomingWindowHours}h`,
        icon: 'arrow-square-in',
        route: '/dealer/bookings',
        query: { tab: 'active' },
      },
      {
        label: this.t('dealerDashboard.approvedNotYetCollected'),
        main: String(d.bookings.approved),
        note: this.t('dealerDashboard.heldForTheirDates'),
        icon: 'calendar-check',
        route: '/dealer/bookings',
        query: { tab: 'upcoming' },
      },
      // The server withholds these two rather than zeroing them, and a withheld tile leads nowhere:
      // Reports is the screen the same grant closes.
      {
        label: this.t('dealerDashboard.revenueThisMonth'),
        main: d.revenueThisMonth
          ? `${d.revenueThisMonth.amount.toLocaleString('en-GB')} ${d.revenueThisMonth.currency}`
          : '—',
        note: d.revenueThisMonth
          ? 'rentals returned this month, before commission'
          : 'not part of your access',
        icon: 'currency-circle-dollar',
        route: d.revenueThisMonth ? '/dealer/reports' : null,
      },
      {
        label: this.t('dealerDashboard.occupancyRate'),
        main: d.occupancyPercentLast30Days === null ? '—' : `${d.occupancyPercentLast30Days}%`,
        note:
          d.occupancyPercentLast30Days === null
            ? 'not part of your access'
            : 'fleet utilisation, last 30 days',
        icon: 'gauge',
        route: d.occupancyPercentLast30Days === null ? null : '/dealer/reports',
      },
    ];
  });

  /**
   * What needs a person today, from facts the platform actually has: requests waiting, handovers
   * due, cars overdue. Nothing invented (the design's "photo upload failed" and "delivery blocked"
   * rows have no source and are not shown).
   */
  protected readonly attention = computed<readonly Attention[]>(() => {
    const d = this.dashboard();
    if (!d) return [];
    const items: Attention[] = [];

    if (d.bookings.requested > 0) {
      items.push({
        type: 'Booking request',
        title: `${d.bookings.requested} booking ${d.bookings.requested === 1 ? 'request is' : 'requests are'} waiting for an answer`,
        desc: d.bookings.oldestRequestedAt
          ? `The oldest was made ${this.ago(d.bookings.oldestRequestedAt)}. A request expires when its rental date arrives unanswered.`
          : '',
        entity: 'Bookings',
        when: d.bookings.oldestRequestedAt ? this.ago(d.bookings.oldestRequestedAt) : '',
        status: 'Pending',
        tone: 'warn',
        action: 'Review',
        route: '/dealer/bookings',
        query: { tab: 'pending' },
      });
    }

    for (const overdue of d.upcomingReturns.filter((r) => r.isOverdue)) {
      items.push({
        type: 'Overdue return',
        title: `${overdue.vehicleLabel} was due back ${this.when(overdue.when)}`,
        desc: `${overdue.customerName} has not returned the car. Record the return when it comes back, and note any damage within the settlement window.`,
        entity: overdue.reference,
        when: this.ago(overdue.when),
        status: 'Overdue',
        tone: 'bad',
        action: 'View booking',
        route: `/dealer/bookings/${overdue.bookingId}`,
      });
    }

    for (const pickup of d.upcomingPickups.slice(0, 2)) {
      items.push({
        type: pickup.pickupMethod === 'Delivery' ? 'Delivery' : 'Pickup',
        title: `${pickup.vehicleLabel} ${pickup.pickupMethod === 'Delivery' ? 'delivery' : 'pickup'} ${this.when(pickup.when)}`,
        desc: `${pickup.customerName}. Record the handover with the odometer and fuel level when the keys change hands.`,
        entity: pickup.reference,
        when: this.until(pickup.when),
        status: pickup.status,
        tone: 'ok',
        action: 'View booking',
        route: `/dealer/bookings/${pickup.bookingId}`,
      });
    }

    for (const ret of d.upcomingReturns.filter((r) => !r.isOverdue).slice(0, 2)) {
      items.push({
        type: 'Return',
        title: `${ret.vehicleLabel} return ${this.when(ret.when)}`,
        desc: `${ret.customerName}'s rental ends. Confirm the return and note any damage within the settlement window.`,
        entity: ret.reference,
        when: this.until(ret.when),
        status: 'Active',
        tone: 'ok',
        action: 'View booking',
        route: `/dealer/bookings/${ret.bookingId}`,
      });
    }

    return items;
  });

  protected readonly fleetStatus = computed(() => {
    const d = this.dashboard();
    if (!d) return [];
    const order = ['Active', 'Hidden', 'Maintenance', 'Draft'];
    const tones: Record<string, Tone> = {
      Active: 'ok',
      Hidden: 'dim',
      Maintenance: 'bad',
      Draft: 'accent',
    };
    const labels: Record<string, string> = {
      Active: 'Published',
      Hidden: 'Hidden',
      Maintenance: 'Off the road',
      Draft: 'Draft',
    };
    return order
      .map((status) => ({
        status,
        count: d.fleetStatus.find((f) => f.status === status)?.count ?? 0,
      }))
      .filter((row) => row.count > 0)
      .map((row) => ({
        label: labels[row.status] ?? row.status,
        n: row.count,
        tone: tones[row.status] ?? 'dim',
        width: d.totalVehicles ? Math.round((row.count / d.totalVehicles) * 100) : 0,
      }));
  });

  protected readonly failure = computed(() => {
    const error = this.resource.error() as
      { status?: number; error?: { code?: string } } | undefined;
    if (!error) return null;
    if (error.error?.code === 'dealer.not_registered')
      return 'This account is not part of a dealership.';
    return 'Your dashboard could not be loaded. Nothing has been changed.';
  });

  protected statusTone(handover: UpcomingHandover): Tone {
    if (handover.isOverdue) return 'bad';
    return handover.status === 'Approved' ? 'accent' : 'ok';
  }

  protected reload(): void {
    this.resource.reload();
    this.bookings.counts.reload();
  }

  protected when(iso: string): string {
    const date = new Date(iso);
    const today = new Date();
    const sameDay = date.toDateString() === today.toDateString();
    const tomorrow = new Date(today);
    tomorrow.setDate(today.getDate() + 1);
    const time = date.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' });
    if (sameDay) return `today ${time}`;
    if (date.toDateString() === tomorrow.toDateString()) return `tomorrow ${time}`;
    return `${date.toLocaleDateString('en-GB', { day: '2-digit', month: 'short' })} ${time}`;
  }

  protected ago(iso: string): string {
    const hours = Math.max(0, Math.round((Date.now() - Date.parse(iso)) / 3_600_000));
    if (hours < 1) return 'just now';
    if (hours < 48) return `${hours}h ago`;
    return `${Math.round(hours / 24)} days ago`;
  }

  protected until(iso: string): string {
    const hours = Math.round((Date.parse(iso) - Date.now()) / 3_600_000);
    if (hours <= 0) return 'now';
    if (hours < 48) return `in ${hours}h`;
    return `in ${Math.round(hours / 24)} days`;
  }

  protected describe(entry: DealerDashboard['recentActivity'][number]): string {
    const verb: Record<string, string> = {
      Approved: 'approved',
      Rejected: 'rejected',
      PickedUp: 'handed over',
      Returned: 'took back',
      Cancelled: 'cancelled',
    };
    return `${entry.actorName} ${verb[entry.toStatus] ?? entry.toStatus.toLowerCase()} booking ${entry.reference}`;
  }
}
