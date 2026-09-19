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
import { FormatService } from '../../core/i18n/format.service';
import { TranslationKey } from '../../core/i18n/en';

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
 * This person's own changes, one whole message per status the booking moved to. Any other status is
 * still worded, through the office's status label.
 */
const ACTIVITY: Readonly<Record<string, TranslationKey>> = {
  Approved: 'employeeDash.activityApproved',
  Rejected: 'employeeDash.activityRejected',
  PickedUp: 'employeeDash.activityHandedOver',
  Returned: 'employeeDash.activityTookBack',
  Cancelled: 'employeeDash.activityCancelled',
};

/** The greeting for the hour, with the name inside the message: Arabic punctuates it differently. */
function greetingKey(hour: number, named: boolean): TranslationKey {
  if (hour < 12) return named ? 'employeeDash.goodMorningName' : 'employeeDash.goodMorning';
  if (hour < 18) return named ? 'employeeDash.goodAfternoonName' : 'employeeDash.goodAfternoon';
  return named ? 'employeeDash.goodEveningName' : 'employeeDash.goodEvening';
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
  private readonly statusLabel = inject(I18nService).statusLabel;
  protected readonly formats = inject(FormatService);
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
    const name = this.firstName();
    return this.t(greetingKey(new Date().getHours(), name !== ''), { name });
  });

  protected readonly kpis = computed<readonly Kpi[]>(() => {
    const d = this.dashboard();
    if (!d) return [];

    // The window the server counted over, never a figure written here.
    const hours = d.upcomingWindowHours;
    const overdue = d.bookings.overdueReturns;
    const next = (handovers: readonly UpcomingHandover[]): string =>
      handovers[0]
        ? this.t('employeeDash.nextWhen', { when: this.formats.dayAndTime(handovers[0].when) })
        : this.t('employeeDash.noneInNextHours', { count: hours });

    return [
      {
        label: this.t('employeeDashboard.pendingRequests'),
        main: this.formats.number(d.bookings.requested),
        // The oldest one is a fact on the record. How long they have to answer is not: nothing in
        // the platform expires a request, so no deadline is claimed here.
        note: d.bookings.oldestRequestedAt
          ? this.t('employeeDash.oldestWhen', {
              when: this.formats.relative(d.bookings.oldestRequestedAt),
            })
          : this.t('employeeDash.nothingWaiting'),
        icon: 'bell-ringing',
        route: '/employee/bookings',
        query: { tab: 'pending' },
      },
      {
        label: this.t('employeeDash.pickupsNextHours', { count: hours }),
        main: this.formats.number(d.upcomingPickups.length),
        note: next(d.upcomingPickups),
        icon: 'arrow-square-out',
        route: '/employee/bookings',
        query: { tab: 'upcoming' },
      },
      {
        label: this.t('employeeDash.returnsNextHours', { count: hours }),
        main: this.formats.number(d.upcomingReturns.length),
        note: next(d.upcomingReturns),
        icon: 'arrow-square-in',
        route: '/employee/bookings',
        query: { tab: 'active' },
      },
      {
        label: this.t('employeeDashboard.activeRentals'),
        main: this.formats.number(d.bookings.pickedUp),
        note:
          overdue > 0
            ? this.t('employeeDash.overdueCount', { count: overdue })
            : this.t('employeeDash.noneOverdue'),
        icon: 'car-simple',
        route: '/employee/bookings',
        query: { tab: 'active' },
      },
      {
        label: this.t('employeeDashboard.confirmedNotYetCollected'),
        main: this.formats.number(d.bookings.confirmed),
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
    // A car or a customer can be gone by the time a handover is due. The server says so with a null
    // rather than an English phrase, and the console words it.
    const vehicle = (handover: UpcomingHandover): string =>
      handover.vehicleLabel ?? this.t('dealerBookings.vehicleNoLongerListed');
    const customer = (handover: UpcomingHandover): string =>
      handover.customerName ?? this.t('common.customerAccountClosed');

    if (d.bookings.requested > 0) {
      const oldest = d.bookings.oldestRequestedAt;
      items.push({
        type: this.t('employeeDash.bookingRequest'),
        title: this.t('employeeDash.requestsWaitingForAnswer', { count: d.bookings.requested }),
        desc: oldest
          ? this.t('employeeDash.oldestArrived', { when: this.formats.relative(oldest) })
          : '',
        entity: this.t('common.bookings'),
        when: oldest ? this.formats.relative(oldest) : '',
        tone: 'warn',
        action: this.t('queue.actionReview'),
        route: '/employee/bookings',
        query: { tab: 'pending' },
      });
    }

    for (const overdue of d.upcomingReturns.filter((r) => r.isOverdue)) {
      items.push({
        type: this.t('employeeDash.returnOverdue'),
        title: this.t('employeeDash.wasDueBack', {
          vehicle: vehicle(overdue),
          when: this.formats.dayAndTime(overdue.when),
        }),
        desc: this.t('employeeDash.overdueDesc', { customer: customer(overdue) }),
        entity: overdue.reference,
        when: this.formats.relative(overdue.when),
        tone: 'bad',
        action: this.t('dealerDecide.return.confirm'),
        route: `/employee/bookings/${overdue.bookingId}`,
      });
    }

    for (const pickup of d.upcomingPickups.slice(0, 3)) {
      const delivery = pickup.pickupMethod === 'Delivery';
      const at = { vehicle: vehicle(pickup), when: this.formats.dayAndTime(pickup.when) };
      items.push({
        type: delivery ? this.t('common.delivery') : this.t('employeeDash.pickupApproaching'),
        title: delivery
          ? this.t('employeeDash.deliveryAt', at)
          : this.t('employeeDash.pickupAt', at),
        desc: this.t('employeeDash.pickupDesc', { customer: customer(pickup) }),
        entity: pickup.reference,
        when: this.formats.relative(pickup.when),
        tone: 'ok',
        action: this.t('dealerDecide.pickup.confirm'),
        route: `/employee/bookings/${pickup.bookingId}`,
      });
    }

    for (const ret of d.upcomingReturns.filter((r) => !r.isOverdue).slice(0, 3)) {
      items.push({
        type: this.t('employeeDash.returnDue'),
        title: this.t('employeeDash.dueBackAt', {
          vehicle: vehicle(ret),
          when: this.formats.dayAndTime(ret.when),
        }),
        desc: this.t('employeeDash.returnDesc', { customer: customer(ret) }),
        entity: ret.reference,
        when: this.formats.relative(ret.when),
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
    const key = ACTIVITY[entry.toStatus];
    return key
      ? this.t(key, { reference: entry.reference })
      : this.t('employeeDash.activityOther', {
          reference: entry.reference,
          status: this.statusLabel(entry.toStatus, 'dealerBooking'),
        });
  }
}
