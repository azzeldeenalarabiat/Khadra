import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { httpResource } from '@angular/common/http';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { KeyValue, Tone } from '../../core/models/console.models';
import { BookingListItem, PagedResult } from '../../core/models/bookings.api';
import { Vehicle, VehicleStatusAction } from '../../core/models/fleet.api';
import { DealerActivityEntry } from '../../core/models/dealer-console.api';
import { FleetService } from '../../core/services/fleet.service';
import { DealerConsoleService } from '../../core/services/dealer-console.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { ImageFallbackDirective } from '../../shared/image-fallback.directive';
import { I18nService } from '../../core/i18n/i18n.service';

type Tab = 'overview' | 'availability' | 'bookings' | 'activity';

interface CalendarDay {
  readonly n: number;
  readonly tag: string;
  readonly tone: Tone;
  readonly bookingId: string | null;
}

/**
 * One car (design `isVehicle`): what it is, what it costs, and what it is doing.
 *
 * Availability is DERIVED from the car's bookings -- Approved ones hold dates, a PickedUp one means
 * it is out now -- and from the car's own status (off the road blocks every day). There is no
 * per-day "blocked" store; the design's "block dates" is served by taking the car off the road.
 * The activity tab lists booking changes on this car; edits to the listing itself are not logged
 * yet, and the tab says so rather than inventing entries.
 */
@Component({
  selector: 'kh-vehicle-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './vehicle-detail.component.html',
  imports: [RouterLink, IconComponent, ImageFallbackDirective],
})
export class VehicleDetailComponent {
  protected readonly t = inject(I18nService).t;
  // Server enum names, in the reader's language. Shared rather than per-component: the same enum
  // shows on half a dozen screens, and a copy each is a copy each to forget a new member in.
  protected readonly statusLabel = inject(I18nService).statusLabel;
  private readonly service = inject(FleetService);
  private readonly consoleData = inject(DealerConsoleService);
  private readonly ui = inject(ConsoleUiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  private readonly vehicleId = toSignal(
    this.route.paramMap.pipe(map((params) => params.get('vehicleId'))),
    { initialValue: this.route.snapshot.paramMap.get('vehicleId') },
  );

  constructor() {
    effect(() => this.service.editing.set(this.vehicleId()));
  }

  protected readonly resource = this.service.vehicle;
  /** Guarded: `value()` throws in the error state, so nothing reads the resource directly. */
  private readonly data = loaded(this.resource);
  protected readonly car = computed(() => this.data() ?? null);
  protected readonly tab = signal<Tab>('overview');
  protected readonly busy = signal(false);
  protected readonly month = signal(startOfMonth(new Date()));

  protected readonly tabs: readonly { key: Tab; label: string }[] = [
    { key: 'overview', label: 'Overview' },
    { key: 'availability', label: 'Availability' },
    { key: 'bookings', label: 'Bookings' },
    { key: 'activity', label: 'Activity' },
  ];

  /** Every booking on this car the dealer may see, newest first. */
  private readonly bookingsResource = httpResource<PagedResult<BookingListItem>>(() => {
    const id = this.vehicleId();
    return id
      ? { url: '/api/v1/bookings', params: { vehicleId: id, page: 1, pageSize: 100 } }
      : undefined;
  });
  protected readonly bookings = computed(() => this.hires()?.items ?? []);

  private readonly activity = this.consoleData.activity;
  private readonly activityPage = loaded(this.activity);
  private readonly hires = loaded(this.bookingsResource);
  private readonly dealer = loaded(this.consoleData.me);

  /**
   * Editing, publishing, hiding, taking off the road and removing are all `ApprovedDealer`.
   *
   * Reading this car is not: any member of staff needs its plate, its photos, its calendar and its
   * booking history to hand it over. So the page stays whole for an employee and loses only the
   * controls — which is the honest shape, because none of those controls would work. `null` until
   * `me` answers, so the owner's own buttons do not blink.
   */
  protected readonly canManage = computed(
    () => this.consoleData.permissions()?.canManageFleet ?? null,
  );
  protected readonly readOnly = computed(
    () => this.canManage() === false && !this.dealer()?.isOwner,
  );

  protected readonly failure = computed(() => {
    const error = this.resource.error() as { status?: number } | undefined;
    if (!error) return null;
    if (error.status === 404) return this.t('vehicleDetail.thatCarIsNot');
    return this.t('vehicleDetail.theCarCouldNot');
  });

  protected readonly current = computed(
    () => this.bookings().find((b) => b.status === 'PickedUp') ?? null,
  );
  protected readonly next = computed(
    () =>
      this.bookings()
        .filter((b) => b.status === 'Confirmed' && Date.parse(b.periodStart) > Date.now())
        .sort((a, b) => Date.parse(a.periodStart) - Date.parse(b.periodStart))[0] ?? null,
  );

  protected readonly pill = computed<{ label: string; tone: Tone }>(() => {
    const c = this.car();
    if (!c) return { label: '', tone: 'dim' };
    if (this.current()) return { label: this.t('vehicleDetail.onHire'), tone: 'accent' };
    if (c.status === 'Maintenance') return { label: this.t('vehicleDetail.offTheRoad'), tone: 'bad' };
    if (c.status === 'Draft') return { label: this.t('status.draft'), tone: 'dim' };
    if (c.status === 'Hidden') return { label: this.t('status.hidden'), tone: 'dim' };
    return c.isBookable ? { label: this.t('status.listed'), tone: 'ok' } : { label: this.t('fleetList.blocked'), tone: 'warn' };
  });

  protected readonly subtitle = computed(() => {
    const c = this.car();
    if (!c) return '';
    return `${c.transmission} · ${c.fuelType} · ${c.seats} seats · plate ${c.plateNumber} · added ${this.date(c.createdAt)}`;
  });

  protected readonly photos = computed(() => {
    const c = this.car();
    if (!c) return [];
    return [...c.images].sort(
      (a, b) => Number(b.isPrimary) - Number(a.isPrimary) || a.position - b.position,
    );
  });

  protected readonly info = computed<readonly KeyValue[]>(() => {
    const c = this.car();
    if (!c) return [];
    return [
      { k: 'Make / model', v: `${c.make} ${c.model}` },
      { k: this.t('common.year'), v: String(c.year) },
      { k: this.t('common.colour'), v: c.color ?? '—' },
      { k: this.t('common.transmission'), v: c.transmission },
      { k: this.t('common.fuel'), v: c.fuelType },
      { k: this.t('common.seats'), v: String(c.seats) },
      { k: this.t('dealerBooking.plate'), v: c.plateNumber },
      { k: this.t('adminUsers.added'), v: this.date(c.createdAt) },
    ];
  });

  protected readonly pricing = computed<readonly KeyValue[]>(() => {
    const c = this.car();
    if (!c) return [];
    const delivery = this.dealer()?.delivery;
    return [
      { k: this.t('vehicleDetail.dailyPrice'), v: `${c.dailyRate.amount} ${c.dailyRate.currency}` },
      { k: this.t('vehicleDetail.securityDeposit'), v: `${c.securityDeposit.amount} ${c.securityDeposit.currency}` },
      {
        k: this.t('vehicleWizard.mileage'),
        v: c.mileage.isUnlimited
          ? 'Unlimited'
          : `${c.mileage.dailyLimitKm} km/day · ${c.mileage.excessFeePerKm?.amount ?? 0} ${c.mileage.excessFeePerKm?.currency ?? ''}/km over`,
      },
      { k: this.t('common.fuelPolicy'), v: c.fuelPolicy === 'FullToFull' ? this.t('vehicleWizard.fullToFull') : this.t('vehicleWizard.sameToSame') },
      {
        k: this.t('common.delivery'),
        v: c.isDeliveryEligible
          ? delivery?.isEnabled
            ? `Eligible · radius ${delivery.radiusKm} km · fee set by the platform`
            : this.t('vehicleDetail.eligibleButDeliveryIs')
          : this.t('vehicleDetail.pickupOnly'),
      },
      { k: this.t('vehicleDetail.insurance'), v: this.t('vehicleDetail.pendingPlatformConfiguration'), tone: 'dim' },
    ];
  });

  protected readonly monthLabel = computed(() =>
    this.month().toLocaleDateString('en-GB', { month: 'long', year: 'numeric' }),
  );

  protected readonly calendar = computed<readonly CalendarDay[]>(() => {
    const c = this.car();
    const first = this.month();
    const days = new Date(first.getFullYear(), first.getMonth() + 1, 0).getDate();
    const holds = this.bookings().filter((b) =>
      ['Requested', 'Approved', 'Confirmed', 'PickedUp'].includes(b.status),
    );
    return Array.from({ length: days }, (_, i) => {
      const dayStart = new Date(first.getFullYear(), first.getMonth(), i + 1).getTime();
      const dayEnd = dayStart + 86_400_000;
      const hold = holds.find(
        (b) => Date.parse(b.periodStart) < dayEnd && Date.parse(b.periodEnd) > dayStart,
      );
      if (hold) {
        if (hold.status === 'PickedUp')
          return { n: i + 1, tag: 'On hire', tone: 'accent', bookingId: hold.bookingId };
        if (hold.status === 'Requested')
          return { n: i + 1, tag: 'Requested', tone: 'bad', bookingId: hold.bookingId };
        if (hold.status === 'Approved')
          return { n: i + 1, tag: this.t('status.awaitingDeposit'), tone: 'bad', bookingId: hold.bookingId };
        return { n: i + 1, tag: hold.reference, tone: 'warn', bookingId: hold.bookingId };
      }
      if (c && c.status === 'Maintenance')
        return { n: i + 1, tag: this.t('status.offTheRoad'), tone: 'dim', bookingId: null };
      if (c && c.status !== 'Active')
        return { n: i + 1, tag: this.t('vehicleDetail.notListed'), tone: 'dim', bookingId: null };
      return { n: i + 1, tag: 'Free', tone: 'ok', bookingId: null };
    });
  });

  protected readonly legend: readonly { label: string; tone: Tone }[] = [
    { label: 'Free', tone: 'ok' },
    { label: 'Booked', tone: 'warn' },
    { label: this.t('vehicleDetail.requestedOrUnpaid'), tone: 'bad' },
    { label: this.t('vehicleDetail.onHire'), tone: 'accent' },
    { label: this.t('vehicleWizard.notOffered'), tone: 'dim' },
  ];

  protected readonly historyNote = computed(() => {
    const all = this.bookings();
    const done = all.filter((b) => b.status === 'Completed' || b.status === 'Returned').length;
    const upcoming = all.filter((b) =>
      ['Requested', 'Approved', 'Confirmed'].includes(b.status),
    ).length;
    return `${done} completed · ${upcoming} upcoming`;
  });

  /** Booking changes on this car, from the dealership's activity feed. */
  protected readonly log = computed<readonly DealerActivityEntry[]>(() => {
    const ids = new Set(this.bookings().map((b) => b.bookingId));
    return (this.activityPage()?.items ?? []).filter((e) => ids.has(e.bookingId));
  });

  protected shiftMonth(delta: number): void {
    const m = this.month();
    this.month.set(new Date(m.getFullYear(), m.getMonth() + delta, 1));
  }

  protected tone(b: BookingListItem): Tone {
    if (b.hasLiveDispute) return 'bad';
    switch (b.status) {
      // Still waiting on somebody: an answer, or a deposit.
      case 'Requested':
      case 'Approved':
        return 'warn';
      case 'Confirmed':
        return 'accent';
      case 'PickedUp':
      case 'Returned':
      case 'Completed':
        return 'ok';
      default:
        return 'dim';
    }
  }

  protected label(b: BookingListItem): string {
    if (b.hasLiveDispute) return 'Disputed';
    return (
      {
        Requested: 'Pending',
        Approved: this.t('status.awaitingDeposit'),
        Confirmed: 'Upcoming',
        PickedUp: 'Active',
        NoShow: 'No-show',
      }[
        b.status as string
      ] ?? b.status
    );
  }

  protected period(b: BookingListItem): string {
    return `${this.date(b.periodStart)} – ${this.date(b.periodEnd)}`;
  }

  protected date(iso: string): string {
    return new Date(iso).toLocaleDateString('en-GB', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
    });
  }

  protected when(iso: string): string {
    return new Date(iso).toLocaleString('en-GB', {
      day: '2-digit',
      month: 'short',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  protected openBooking(id: string | null): void {
    if (id) void this.router.navigate(['/dealer/bookings', id]);
  }

  protected primaryAction(): {
    label: string;
    action: VehicleStatusAction;
    icon: 'eye-slash' | 'eye' | 'check-circle';
  } | null {
    const c = this.car();
    if (!c) return null;
    if (c.status === 'Active') return { label: this.t('fleetList.hide'), action: 'Hide', icon: 'eye-slash' };
    if (c.status === 'Maintenance')
      return { label: this.t('vehicleDetail.backOnTheRoad'), action: 'ReturnFromMaintenance', icon: 'check-circle' };
    return { label: this.t('fleetList.publish'), action: 'Publish', icon: 'eye' };
  }

  protected async changeStatus(action: VehicleStatusAction): Promise<void> {
    const c = this.car();
    if (!c || this.busy()) return;
    this.busy.set(true);
    try {
      await this.service.changeStatus(c.vehicleId, action);
      this.resource.reload();
      this.service.refresh();
      // Four actions, four outcomes. Returning from the garage lands on Hidden by design
      // (Vehicle.ReturnFromMaintenance), so it must not claim the car is bookable again.
      const told: Record<VehicleStatusAction, readonly [string, string]> = {
        Publish: ['Published', this.t('vehicleDetail.customersCanSeeIt')],
        Hide: ['Hidden', this.t('vehicleDetail.customersNoLongerSee')],
        SendToMaintenance: [this.t('status.offTheRoad'), this.t('vehicleDetail.itIsNotOffered')],
        ReturnFromMaintenance: [this.t('fleetList.backOnTheRoad'), this.t('vehicleDetail.itIsHiddenUntil')],
      };
      const [title, body] = told[action];
      this.ui.showToast(title, body);
    } catch (error) {
      const problem = error as { error?: { code?: string; title?: string } };
      this.ui.showToast(
        this.t('vehicleDetail.thatDidNotGo'),
        problem.error?.code === 'vehicle.no_photos'
          ? this.t('vehicleDetail.addAtLeastOne')
          : (problem.error?.title ?? this.t('vehicleDetail.theServiceDidNot')),
        'bad',
      );
    } finally {
      this.busy.set(false);
    }
  }

  protected takeOffRoad(): void {
    const c = this.car();
    if (!c) return;
    this.ui.openAction(
      {
        icon: 'gear',
        tone: 'warn',
        title: `Take ${c.make} ${c.model} off the road?`,
        body: this.t('vehicleDetail.everyDayShowsAs'),
        confirm: this.t('fleetList.takeOffTheRoad'),
        result: {
          title: this.t('vehicleDetail.offTheRoad'),
          body: `${c.make} ${c.model} is not being offered.`,
          tone: 'warn',
        },
      },
      async () => {
        await this.service.changeStatus(c.vehicleId, 'SendToMaintenance');
        this.resource.reload();
        this.service.refresh();
      },
      { title: this.t('vehicleDetail.offTheRoad'), body: `${c.make} ${c.model} is not being offered.`, tone: 'warn' },
    );
  }

  protected reload(): void {
    this.resource.reload();
    this.bookingsResource.reload();
  }
}

function startOfMonth(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), 1);
}
