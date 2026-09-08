import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Tone } from '../../core/models/console.models';
import { DealerActivityEntry, UpcomingHandover } from '../../core/models/dealer-console.api';
import { DealerConsoleService } from '../../core/services/dealer-console.service';
import { DealerBookingsService } from '../../core/services/dealer-bookings.service';
import { SessionService } from '../../core/services/session.service';
import { loaded } from '../../core/services/loaded';
import { IconName } from '../../shared/icon/icon-paths';
import { IconComponent } from '../../shared/icon/icon.component';
import { I18nService } from '../../core/i18n/i18n.service';

interface Kpi {
  readonly label: string;
  readonly main: string;
  readonly note: string;
  readonly icon: IconName;
  readonly route: string;
  readonly query?: Record<string, string>;
}

interface Attention {
  readonly type: string;
  readonly title: string;
  readonly desc: string;
  readonly entity: string;
  readonly when: string;
  readonly tone: Tone;
  readonly action: string;
  readonly route: string;
  readonly query?: Record<string, string>;
}

/**
 * The employee's dashboard (design: Employee Console, `isDashboard`).
 *
 * The owner's dashboard answers "how is the business doing" — revenue, occupancy, fleet mix. This
 * one answers "what do I have to do today", and every tile is a handover: requests to answer, cars
 * going out, cars coming back, cars already out, cars booked. Nothing financial appears, and not
 * because it is hidden — an employee without the reports grant is sent `null` for those figures by
 * the server, and a tile that permanently reads "—" is worth less than the space it takes.
 *
 * Two things the design draws are NOT here, because the platform has no source for them:
 *   · "48h limit" beside the pending count. There is no booking-request expiry rule in this system
 *     (`BusinessRules` has `AdminSlaHours`, which is the admin's clock for reviewing a dealer
 *     application — a different thing entirely). Printing it would invent a promise to a customer.
 *   · "Today's" pickups and returns. The server answers over a window it chooses and names
 *     (`upcomingWindowHours`), which is not the same as a calendar day, so the tiles say the window
 *     the figure was actually counted over.
 */
@Component({
  selector: 'kh-employee-dashboard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './employee-dashboard.component.html',
  imports: [RouterLink, IconComponent],
})
export class EmployeeDashboardComponent {
  protected readonly t = inject(I18nService).t;
  private readonly console = inject(DealerConsoleService);
  private readonly bookings = inject(DealerBookingsService);
  private readonly session = inject(SessionService);

  protected readonly resource = this.console.dashboard;
  /** Guarded: `value()` throws in the error state, so nothing reads the resource directly. */
  private readonly data = loaded(this.resource);
  protected readonly dashboard = computed(() => this.data() ?? null);

  protected readonly firstName = computed(
    () => this.session.user()?.fullName?.trim().split(/\s+/)[0] ?? '',
  );

  protected readonly greeting = computed(() => {
    const hour = new Date().getHours();
    const part = hour < 12 ? this.t('employeeDash.goodMorning') : hour < 18 ? this.t('employeeDash.goodAfternoon') : this.t('employeeDash.goodEvening');
    const name = this.firstName();
    return name ? `${part}, ${name}` : part;
  });

  protected readonly kpis = computed<readonly Kpi[]>(() => {
    const d = this.dashboard();
    if (!d) return [];

    const window = `${d.upcomingWindowHours}h`;
    const overdue = d.bookings.overdueReturns;

    return [
      {
        label: this.t('employeeDashboard.pendingRequests'),
        main: String(d.bookings.requested),
        // The oldest one is a fact on the record. How long they have to answer is not: nothing in
        // the platform expires a request, so no deadline is claimed here.
        note: d.bookings.oldestRequestedAt
          ? `oldest ${this.ago(d.bookings.oldestRequestedAt)}`
          : this.t('employeeDash.nothingWaiting'),
        icon: 'bell-ringing',
        route: '/employee/bookings',
        query: { tab: 'pending' },
      },
      {
        label: `Pickups · next ${window}`,
        main: String(d.upcomingPickups.length),
        note: d.upcomingPickups[0]
          ? `next ${this.when(d.upcomingPickups[0].when)}`
          : `none in the next ${window}`,
        icon: 'arrow-square-out',
        route: '/employee/bookings',
        query: { tab: 'upcoming' },
      },
      {
        label: `Returns · next ${window}`,
        main: String(d.upcomingReturns.length),
        note: d.upcomingReturns[0]
          ? `next ${this.when(d.upcomingReturns[0].when)}`
          : `none in the next ${window}`,
        icon: 'arrow-square-in',
        route: '/employee/bookings',
        query: { tab: 'active' },
      },
      {
        label: this.t('employeeDashboard.activeRentals'),
        main: String(d.bookings.pickedUp),
        note: overdue > 0 ? `${overdue} overdue` : this.t('employeeDash.noneOverdue'),
        icon: 'car-simple',
        route: '/employee/bookings',
        query: { tab: 'active' },
      },
      {
        label: this.t('employeeDashboard.confirmedNotYetCollected'),
        main: String(d.bookings.confirmed),
        note: this.t('employeeDashboard.heldForTheirDates'),
        icon: 'calendar-check',
        route: '/employee/bookings',
        query: { tab: 'upcoming' },
      },
    ];
  });

  /**
   * What needs a person today, from facts the platform actually has.
   *
   * The design's "delivery 27 km outside the radius" row is not here: the API does not report a
   * booking as out-of-radius, and the radius check happens before a request is ever accepted.
   */
  protected readonly attention = computed<readonly Attention[]>(() => {
    const d = this.dashboard();
    if (!d) return [];
    const items: Attention[] = [];

    if (d.bookings.requested > 0) {
      const plural = d.bookings.requested === 1 ? this.t('employeeDash.requestIs') : this.t('employeeDash.requestsAre');
      items.push({
        type: this.t('employeeDash.bookingRequest'),
        title: `${d.bookings.requested} ${plural} waiting for an answer`,
        desc: d.bookings.oldestRequestedAt
          ? `The oldest arrived ${this.ago(d.bookings.oldestRequestedAt)}.`
          : '',
        entity: 'Bookings',
        when: d.bookings.oldestRequestedAt ? this.ago(d.bookings.oldestRequestedAt) : '',
        tone: 'warn',
        action: 'Review',
        route: '/employee/bookings',
        query: { tab: 'pending' },
      });
    }

    for (const overdue of d.upcomingReturns.filter((r) => r.isOverdue)) {
      items.push({
        type: this.t('employeeDash.returnOverdue'),
        title: `${overdue.vehicleLabel} was due back ${this.when(overdue.when)}`,
        desc: `${overdue.customerName} has not brought the car back. Record the return when it arrives.`,
        entity: overdue.reference,
        when: this.ago(overdue.when),
        tone: 'bad',
        action: this.t('dealerDecide.return.confirm'),
        route: `/employee/bookings/${overdue.bookingId}`,
      });
    }

    for (const pickup of d.upcomingPickups.slice(0, 3)) {
      const delivery = pickup.pickupMethod === 'Delivery';
      items.push({
        type: delivery ? 'Delivery' : this.t('employeeDash.pickupApproaching'),
        title: `${pickup.vehicleLabel} ${delivery ? 'delivery' : 'pickup'} ${this.when(pickup.when)}`,
        desc: `${pickup.customerName}. Record the handover when the car leaves.`,
        entity: pickup.reference,
        when: this.until(pickup.when),
        tone: 'ok',
        action: this.t('dealerDecide.pickup.confirm'),
        route: `/employee/bookings/${pickup.bookingId}`,
      });
    }

    for (const ret of d.upcomingReturns.filter((r) => !r.isOverdue).slice(0, 3)) {
      items.push({
        type: this.t('employeeDash.returnDue'),
        title: `${ret.vehicleLabel} due back ${this.when(ret.when)}`,
        desc: `${ret.customerName}'s rental ends. Confirm the return and note any damage.`,
        entity: ret.reference,
        when: this.until(ret.when),
        tone: 'ok',
        action: this.t('dealerDecide.return.confirm'),
        route: `/employee/bookings/${ret.bookingId}`,
      });
    }

    return items;
  });

  /**
   * "Recorded against you" — this employee's own actions, and only theirs.
   *
   * Filtered by the SERVER (`GET /dealers/me/activity?actor=me`), not here. The dealership's trail is
   * paged, so filtering a fetched page in the browser would silently drop everything past the first
   * one and show someone an incomplete record of what they had done — worse than showing the shared
   * trail honestly.
   */
  private readonly mine = loaded(this.console.myActivity);
  protected readonly activity = computed(() => this.mine()?.items ?? []);

  protected readonly failure = computed(() => {
    const error = this.resource.error() as
      { status?: number; error?: { code?: string } } | undefined;
    if (!error) return null;
    if (error.error?.code === 'dealer.not_registered')
      return this.t('employeeDash.thisAccountIsNot');
    return this.t('employeeDash.yourDashboardCouldNot');
  });

  protected reload(): void {
    this.resource.reload();
    this.bookings.counts.reload();
  }

  protected statusTone(handover: UpcomingHandover): Tone {
    if (handover.isOverdue) return 'bad';
    // Approved is still waiting on the customer's deposit; only a confirmed booking is a car to
    // have ready.
    if (handover.status === 'Approved') return 'warn';
    return handover.status === 'Confirmed' ? 'accent' : 'ok';
  }

  /** Every row is already this person's, so the sentence does not repeat their name back at them. */
  protected describe(entry: DealerActivityEntry): string {
    const verb: Record<string, string> = {
      Approved: 'Approved',
      Rejected: 'Rejected',
      PickedUp: this.t('employeeDash.handedOver'),
      Returned: this.t('employeeDash.tookBack'),
      Cancelled: 'Cancelled',
    };
    return `${verb[entry.toStatus] ?? entry.toStatus} ${entry.reference}`;
  }

  protected when(iso: string): string {
    const date = new Date(iso);
    const today = new Date();
    const tomorrow = new Date(today);
    tomorrow.setDate(today.getDate() + 1);
    const time = date.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' });
    if (date.toDateString() === today.toDateString()) return `today ${time}`;
    if (date.toDateString() === tomorrow.toDateString()) return `tomorrow ${time}`;
    return `${date.toLocaleDateString('en-GB', { day: '2-digit', month: 'short' })} ${time}`;
  }

  protected ago(iso: string): string {
    const hours = Math.max(0, Math.round((Date.now() - Date.parse(iso)) / 3_600_000));
    if (hours < 1) return this.t('employeeDash.justNow');
    if (hours < 48) return `${hours}h ago`;
    return `${Math.round(hours / 24)} days ago`;
  }

  protected until(iso: string): string {
    const hours = Math.round((Date.parse(iso) - Date.now()) / 3_600_000);
    if (hours <= 0) return 'now';
    if (hours < 48) return `in ${hours}h`;
    return `in ${Math.round(hours / 24)} days`;
  }
}
