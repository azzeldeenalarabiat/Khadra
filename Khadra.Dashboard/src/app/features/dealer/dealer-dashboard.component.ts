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
import { FormatService } from '../../core/i18n/format.service';
import { TranslationKey } from '../../core/i18n/en';

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
  /** The pill: the booking's status in the office's own words, or "Overdue", which is no status. */
  readonly pill: string;
  readonly tone: Tone;
  readonly action: string;
  readonly route: string;
  readonly query?: Record<string, string>;
}

/**
 * The fleet mix, in the order it is drawn, and the tone of each. Machine data only: the words come
 * from `statusLabel(status, 'vehicle')`, which is what calls an `Active` car "Published".
 */
const FLEET_TONES: Readonly<Record<string, Tone>> = {
  Active: 'ok',
  Hidden: 'dim',
  Maintenance: 'bad',
  Draft: 'accent',
};

/**
 * The activity sentence for each status a booking moved to, one whole message per verb: Arabic puts
 * the actor and the booking where English does not. Any other status is still worded, through the
 * office's status label.
 */
const ACTIVITY: Readonly<Record<string, TranslationKey>> = {
  Approved: 'dealerDash.activityApproved',
  Rejected: 'dealerDash.activityRejected',
  PickedUp: 'dealerDash.activityHandedOver',
  Returned: 'dealerDash.activityTookBack',
  Cancelled: 'dealerDash.activityCancelled',
};

/** The greeting for the hour, with the name inside the message: Arabic punctuates it differently. */
function greetingKey(hour: number, named: boolean): TranslationKey {
  if (hour < 12) return named ? 'employeeDash.goodMorningName' : 'employeeDash.goodMorning';
  if (hour < 18) return named ? 'employeeDash.goodAfternoonName' : 'employeeDash.goodAfternoon';
  return named ? 'employeeDash.goodEveningName' : 'employeeDash.goodEvening';
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
  // Server enum names, in the reader's language. Shared rather than per-component: the same enum
  // shows on half a dozen screens, and a copy each is a copy each to forget a new member in.
  protected readonly statusLabel = inject(I18nService).statusLabel;
  private readonly console = inject(DealerConsoleService);
  private readonly bookings = inject(DealerBookingsService);
  private readonly session = inject(SessionService);
  protected readonly formats = inject(FormatService);

  protected readonly resource = this.console.dashboard;
  /** Guarded: `value()` throws in the error state, so nothing reads the resource directly. */
  private readonly data = loaded(this.resource);
  protected readonly dashboard = computed(() => this.data() ?? null);

  /** Both "add a car" entry points on this screen lead into an owner-only form. */
  protected readonly canManageFleet = computed(
    () => this.console.permissions()?.canManageFleet === true,
  );

  protected readonly greeting = computed(() => {
    const name = this.dashboard()?.businessName ?? this.session.user()?.fullName ?? '';
    return this.t(greetingKey(new Date().getHours(), name !== ''), { name });
  });

  protected readonly statusLine = computed(() => {
    const d = this.dashboard();
    if (!d) return '';
    // Three counts and a window in one sentence. Each count picks its own noun form and the window
    // picks the sentence's: Arabic agrees every noun with its own number, so no single count can
    // choose the grammar for the other three.
    return this.t('dealerDash.statusLine', {
      count: d.upcomingWindowHours,
      requests: this.t('dealerDash.bookingRequestsCount', { count: d.bookings.requested }),
      pickups: this.t('dealerDash.pickupsCount', { count: d.upcomingPickups.length }),
      returns: this.t('dealerDash.returnsCount', { count: d.upcomingReturns.length }),
    });
  });

  protected readonly kpis = computed<readonly Kpi[]>(() => {
    const d = this.dashboard();
    if (!d) return [];
    const oldest = d.bookings.oldestRequestedAt
      ? this.t('employeeDash.oldestWhen', {
          when: this.formats.relative(d.bookings.oldestRequestedAt),
        })
      : this.t('employeeDash.nothingWaiting');
    // The window is the server's (`upcomingWindowHours`), so the tile says the span it counted over.
    const next = (handovers: readonly UpcomingHandover[]): string =>
      handovers[0]
        ? this.t('employeeDash.nextWhen', { when: this.formats.dayAndTime(handovers[0].when) })
        : this.t('employeeDash.noneInNextHours', { count: d.upcomingWindowHours });
    return [
      {
        label: this.t('dealerDashboard.pendingRequests'),
        main: this.formats.number(d.bookings.requested),
        note: `${oldest} · ${this.t('dealerDash.answerBeforePickup')}`,
        icon: 'bell-ringing',
        route: '/dealer/bookings',
        query: { tab: 'pending' },
      },
      {
        label: this.t('dealerDashboard.activeRentals'),
        main: this.formats.number(d.bookings.pickedUp),
        note: d.bookings.overdueReturns
          ? this.t('employeeDash.overdueCount', { count: d.bookings.overdueReturns })
          : this.t('dealerDash.allWithinTheirDates'),
        icon: 'car-profile',
        route: '/dealer/bookings',
        query: { tab: 'active' },
      },
      {
        label: this.t('dealerDashboard.availableVehicles'),
        main: this.formats.number(d.availableVehicles),
        note: `${this.t('dealerDash.ofPublished', { count: d.publishedVehicles })} · ${this.t('dealerDash.inYourFleet', { count: d.totalVehicles })}`,
        icon: 'check-square',
        route: '/dealer/fleet',
      },
      {
        label: this.t('dealerDashboard.upcomingPickups'),
        main: this.formats.number(d.upcomingPickups.length),
        note: next(d.upcomingPickups),
        icon: 'arrow-square-out',
        route: '/dealer/bookings',
        query: { tab: 'upcoming' },
      },
      {
        label: this.t('dealerDashboard.upcomingReturns'),
        main: this.formats.number(d.upcomingReturns.length),
        note: next(d.upcomingReturns),
        icon: 'arrow-square-in',
        route: '/dealer/bookings',
        query: { tab: 'active' },
      },
      {
        label: this.t('dealerDashboard.confirmedNotYetCollected'),
        main: this.formats.number(d.bookings.confirmed),
        note: this.t('dealerDashboard.heldForTheirDates'),
        icon: 'calendar-check',
        route: '/dealer/bookings',
        query: { tab: 'upcoming' },
      },
      {
        label: this.t('dealerDashboard.awaitingDeposit'),
        main: this.formats.number(d.bookings.awaitingDeposit),
        note: this.t('dealerDashboard.approvedAndUnpaid'),
        icon: 'clock',
        route: '/dealer/bookings',
        query: { tab: 'upcoming' },
      },
      // The server withholds these two rather than zeroing them, and a withheld tile leads nowhere:
      // Reports is the screen the same grant closes.
      {
        label: this.t('dealerDashboard.revenueThisMonth'),
        // At the currency's own scale and in the reader's locale, like every other amount.
        main: d.revenueThisMonth
          ? this.formats.money(d.revenueThisMonth.amount, d.revenueThisMonth.currency)
          : '—',
        note: d.revenueThisMonth
          ? this.t('dealerDash.rentalsReturnedThisMonth')
          : this.t('dealerDash.notPartOfYour'),
        icon: 'currency-circle-dollar',
        route: d.revenueThisMonth ? '/dealer/reports' : null,
      },
      {
        label: this.t('dealerDashboard.occupancyRate'),
        // A withheld figure (null) prints as "—".
        main: this.formats.percent(d.occupancyPercentLast30Days),
        note:
          d.occupancyPercentLast30Days === null
            ? this.t('dealerDash.notPartOfYour')
            : this.t('dealerDash.fleetUtilisationLast30'),
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
          ? this.t('dealerDash.oldestMadeExpiry', { when: this.formats.relative(oldest) })
          : '',
        entity: this.t('common.bookings'),
        when: oldest ? this.formats.relative(oldest) : '',
        pill: this.statusLabel('Requested', 'dealerBooking'),
        tone: 'warn',
        action: this.t('queue.actionReview'),
        route: '/dealer/bookings',
        query: { tab: 'pending' },
      });
    }

    for (const overdue of d.upcomingReturns.filter((r) => r.isOverdue)) {
      items.push({
        type: this.t('dealerDash.overdueReturn'),
        title: this.t('employeeDash.wasDueBack', {
          vehicle: vehicle(overdue),
          when: this.formats.dayAndTime(overdue.when),
        }),
        desc: this.t('dealerDash.overdueDesc', { customer: customer(overdue) }),
        entity: overdue.reference,
        when: this.formats.relative(overdue.when),
        pill: this.t('time.overdue'),
        tone: 'bad',
        action: this.t('dealerDash.viewBooking'),
        route: `/dealer/bookings/${overdue.bookingId}`,
      });
    }

    for (const pickup of d.upcomingPickups.slice(0, 2)) {
      const delivery = pickup.pickupMethod === 'Delivery';
      const at = { vehicle: vehicle(pickup), when: this.formats.dayAndTime(pickup.when) };
      items.push({
        type: delivery ? this.t('common.delivery') : this.t('handoverType.pickup'),
        title: delivery
          ? this.t('employeeDash.deliveryAt', at)
          : this.t('employeeDash.pickupAt', at),
        desc: this.t('dealerDash.pickupDesc', { customer: customer(pickup) }),
        entity: pickup.reference,
        when: this.formats.relative(pickup.when),
        pill: this.statusLabel(pickup.status, 'dealerBooking'),
        tone: 'ok',
        action: this.t('dealerDash.viewBooking'),
        route: `/dealer/bookings/${pickup.bookingId}`,
      });
    }

    for (const ret of d.upcomingReturns.filter((r) => !r.isOverdue).slice(0, 2)) {
      items.push({
        type: this.t('handoverType.return'),
        title: this.t('dealerDash.returnAt', {
          vehicle: vehicle(ret),
          when: this.formats.dayAndTime(ret.when),
        }),
        desc: this.t('dealerDash.returnDesc', { customer: customer(ret) }),
        entity: ret.reference,
        when: this.formats.relative(ret.when),
        pill: this.statusLabel(ret.status, 'dealerBooking'),
        tone: 'ok',
        action: this.t('dealerDash.viewBooking'),
        route: `/dealer/bookings/${ret.bookingId}`,
      });
    }

    return items;
  });

  protected readonly fleetStatus = computed(() => {
    const d = this.dashboard();
    if (!d) return [];
    return Object.entries(FLEET_TONES)
      .map(([status, tone]) => ({
        status,
        tone,
        count: d.fleetStatus.find((f) => f.status === status)?.count ?? 0,
      }))
      .filter((row) => row.count > 0)
      .map((row) => ({
        status: row.status,
        label: this.statusLabel(row.status, 'vehicle'),
        n: this.formats.number(row.count),
        tone: row.tone,
        width: d.totalVehicles ? Math.round((row.count / d.totalVehicles) * 100) : 0,
      }));
  });

  protected readonly failure = computed(() => {
    const error = this.resource.error() as
      { status?: number; error?: { code?: string } } | undefined;
    if (!error) return null;
    if (error.error?.code === 'dealer.not_registered')
      return this.t('employeeDash.thisAccountIsNot');
    return this.t('employeeDash.yourDashboardCouldNot');
  });

  protected statusTone(handover: UpcomingHandover): Tone {
    if (handover.isOverdue) return 'bad';
    // An approved pickup is still waiting on the customer's deposit, so it is not yet a rental the
    // gallery should be getting a car ready for.
    if (handover.status === 'Approved') return 'warn';
    return handover.status === 'Confirmed' ? 'accent' : 'ok';
  }

  protected reload(): void {
    this.resource.reload();
    this.bookings.counts.reload();
  }

  /**
   * One line of the activity feed. Who acted is sent as facts: no user id is the rental office
   * itself, and an id without a name is somebody whose account has since closed.
   */
  protected describe(entry: DealerDashboard['recentActivity'][number]): string {
    const actor =
      entry.actorUserId === null
        ? this.t('common.theRentalOffice')
        : (entry.actorName ?? this.t('common.formerStaffMember'));
    const key = ACTIVITY[entry.toStatus];
    return key
      ? this.t(key, { actor, reference: entry.reference })
      : this.t('dealerDash.activityOther', {
          actor,
          reference: entry.reference,
          status: this.statusLabel(entry.toStatus, 'dealerBooking'),
        });
  }
}
