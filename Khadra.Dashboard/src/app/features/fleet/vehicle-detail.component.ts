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
import { TranslationKey } from '../../core/i18n/en';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { serverSentence, snapshotProblem } from '../../core/i18n/problem';
import { spellEnumName } from '../../core/i18n/status-key';

type Tab = 'overview' | 'availability' | 'bookings' | 'activity';

interface CalendarDay {
  readonly n: number;
  readonly tag: string;
  readonly tone: Tone;
  readonly bookingId: string | null;
}

/**
 * The tabs, as the machine value the page switches on and the key that words it. The words are
 * chosen when the tabs render: a field initialiser would word them once, in whichever language the
 * page opened in.
 */
const TABS: readonly { readonly key: Tab; readonly label: TranslationKey }[] = [
  { key: 'overview', label: 'vehicleDetail.tabOverview' },
  { key: 'availability', label: 'vehicleDetail.tabAvailability' },
  { key: 'bookings', label: 'common.bookings' },
  { key: 'activity', label: 'vehicleDetail.tabActivity' },
];

/** What each colour on the calendar means, for the same reason as the tabs. */
const LEGEND: readonly { readonly label: TranslationKey; readonly tone: Tone }[] = [
  { label: 'vehicleDetail.free', tone: 'ok' },
  { label: 'vehicleDetail.booked', tone: 'warn' },
  { label: 'vehicleDetail.requestedOrUnpaid', tone: 'bad' },
  { label: 'vehicleDetail.onHire', tone: 'accent' },
  { label: 'vehicleDetail.notOffered', tone: 'dim' },
];

/**
 * The server's transmission and fuel names, and the keys that word them — the same two tables the
 * fleet list words its cards with. The name is what the car carries; the words are the reader's,
 * and the Arabic is the wording `/api/v1/app-config` publishes for the customer app.
 */
const TRANSMISSIONS: Readonly<Record<string, TranslationKey>> = {
  Automatic: 'fleetList.transmissionAutomatic',
  Manual: 'fleetList.transmissionManual',
};
const FUEL_TYPES: Readonly<Record<string, TranslationKey>> = {
  Petrol: 'fleetList.fuelPetrol',
  Diesel: 'fleetList.fuelDiesel',
  Hybrid: 'fleetList.fuelHybrid',
  Electric: 'fleetList.fuelElectric',
};

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
  private readonly i18n = inject(I18nService);
  protected readonly t = this.i18n.t;
  // Server enum names, in the reader's language. Shared rather than per-component: the same enum
  // shows on half a dozen screens, and a copy each is a copy each to forget a new member in.
  private readonly statusLabel = this.i18n.statusLabel;
  protected readonly formats = inject(FormatService);
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

  /** The tabs in the reader's language. */
  protected readonly tabs = computed(() =>
    TABS.map((option) => ({ key: option.key, label: this.t(option.label) })),
  );

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

  /** Why the car did not load, held as facts and worded in `failure`, so a switch re-words it. */
  private readonly problem = computed(() => {
    const error = this.resource.error();
    return error ? snapshotProblem(error) : null;
  });

  protected readonly failure = computed(() => {
    const problem = this.problem();
    if (!problem) return null;
    if (problem.status === 404) return this.t('vehicleDetail.thatCarIsNot');
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
    if (c.status === 'Maintenance')
      return { label: this.t('vehicleDetail.offTheRoad'), tone: 'bad' };
    if (c.status === 'Draft') return { label: this.t('status.draft'), tone: 'dim' };
    if (c.status === 'Hidden') return { label: this.t('status.hidden'), tone: 'dim' };
    return c.isBookable
      ? { label: this.t('status.listed'), tone: 'ok' }
      : { label: this.t('fleetList.blocked'), tone: 'warn' };
  });

  /** Independent facts about the car, each a whole message, read as a list. */
  protected readonly subtitle = computed(() => {
    const c = this.car();
    if (!c) return '';
    return [
      this.transmissionName(c),
      this.fuelName(c),
      this.t('fleetList.seatCount', { count: c.seats }),
      this.t('vehicleDetail.plateFact', { plate: c.plateNumber }),
      this.t('vehicleDetail.addedFact', { date: this.formats.date(c.createdAt) }),
    ].join(' · ');
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
      { k: this.t('vehicleDetail.makeModel'), v: `${c.make} ${c.model}` },
      { k: this.t('common.year'), v: String(c.year) },
      { k: this.t('common.colour'), v: c.color ?? '—' },
      { k: this.t('common.transmission'), v: this.transmissionName(c) },
      { k: this.t('common.fuel'), v: this.fuelName(c) },
      { k: this.t('common.seats'), v: this.formats.number(c.seats) },
      { k: this.t('dealerBooking.plate'), v: c.plateNumber },
      { k: this.t('vehicleDetail.dateAdded'), v: this.formats.date(c.createdAt) },
    ];
  });

  protected readonly pricing = computed<readonly KeyValue[]>(() => {
    const c = this.car();
    if (!c) return [];
    const delivery = this.dealer()?.delivery;
    const excessFee = c.mileage.excessFeePerKm;
    return [
      {
        k: this.t('vehicleDetail.dailyPrice'),
        v: this.formats.money(c.dailyRate.amount, c.dailyRate.currency),
      },
      {
        k: this.t('vehicleDetail.securityDeposit'),
        v: this.formats.money(c.securityDeposit.amount, c.securityDeposit.currency),
      },
      {
        k: this.t('vehicleWizard.mileage'),
        // A limited policy always carries both figures (`MileagePolicy.Limited`); a missing one
        // prints as a dash, never as an invented zero.
        v: c.mileage.isUnlimited
          ? this.t('vehicleWizard.unlimited')
          : this.t('dealerBooking.mileageAllowance', {
              limit: this.formats.number(c.mileage.dailyLimitKm),
              fee: this.formats.money(excessFee?.amount, excessFee?.currency),
            }),
      },
      {
        k: this.t('common.fuelPolicy'),
        v:
          c.fuelPolicy === 'FullToFull'
            ? this.t('vehicleWizard.fullToFull')
            : this.t('vehicleWizard.sameToSame'),
      },
      {
        k: this.t('common.delivery'),
        // The radius and the fee are this dealership's own settings (`/dealer/delivery`), read from
        // `me` — the platform sets neither.
        v: c.isDeliveryEligible
          ? delivery?.isEnabled
            ? this.t('vehicleDetail.deliveryEligibleRadiusFee', {
                radius: this.formats.number(delivery.radiusKm),
                fee: this.formats.money(delivery.fee?.amount, delivery.fee?.currency),
              })
            : this.t('vehicleDetail.eligibleButDeliveryIs')
          : this.t('vehicleDetail.pickupOnly'),
      },
      {
        k: this.t('vehicleDetail.insurance'),
        v: this.t('vehicleDetail.pendingPlatformConfiguration'),
        tone: 'dim',
      },
    ];
  });

  protected readonly monthLabel = computed(() => this.formats.monthYear(this.month()));

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
          return {
            n: i + 1,
            tag: this.t('vehicleDetail.onHire'),
            tone: 'accent',
            bookingId: hold.bookingId,
          };
        if (hold.status === 'Requested')
          return {
            n: i + 1,
            tag: this.t('vehicleDetail.requested'),
            tone: 'bad',
            bookingId: hold.bookingId,
          };
        if (hold.status === 'Approved')
          return {
            n: i + 1,
            tag: this.t('status.awaitingDeposit'),
            tone: 'bad',
            bookingId: hold.bookingId,
          };
        return { n: i + 1, tag: hold.reference, tone: 'warn', bookingId: hold.bookingId };
      }
      if (c && c.status === 'Maintenance')
        return { n: i + 1, tag: this.t('status.offTheRoad'), tone: 'dim', bookingId: null };
      if (c && c.status !== 'Active')
        return { n: i + 1, tag: this.t('vehicleDetail.notListed'), tone: 'dim', bookingId: null };
      return { n: i + 1, tag: this.t('vehicleDetail.free'), tone: 'ok', bookingId: null };
    });
  });

  /** The legend in the reader's language. */
  protected readonly legend = computed(() =>
    LEGEND.map((item) => ({ label: this.t(item.label), tone: item.tone })),
  );

  /** Two counts, each its own plural message: Arabic words "3 completed" and "11 completed" apart. */
  protected readonly historyNote = computed(() => {
    const all = this.bookings();
    const done = all.filter((b) => b.status === 'Completed' || b.status === 'Returned').length;
    const upcoming = all.filter((b) =>
      ['Requested', 'Approved', 'Confirmed'].includes(b.status),
    ).length;
    return [
      this.t('vehicleDetail.completedCount', { count: done }),
      this.t('vehicleDetail.upcomingCount', { count: upcoming }),
    ].join(' · ');
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

  /**
   * The dealer's word for the booking's state, as the bookings list says it. A live dispute is a flag
   * with its own key; everything else is the dealer-scoped status, where `Requested` is "Pending" and
   * `PickedUp` is "Active".
   */
  protected label(b: BookingListItem): string {
    if (b.hasLiveDispute) return this.t('status.disputed');
    return this.statusLabel(b.status, 'dealerBooking');
  }

  /** The customer's name, or the fact that the account was closed. Never the English stand-in. */
  protected customerName(b: BookingListItem): string {
    return b.customerAccountClosed ? this.t('common.customerAccountClosed') : b.customerName;
  }

  /**
   * "06 Sept 2026 – 09 Sept 2026", as one message.
   *
   * The Arabic says "من … إلى …" rather than pointing an arrow, and that is load-bearing: this range
   * is also a `{period}` inside the rental sentences, where the dictionary isolates it. An isolate
   * takes its direction from its first strong letter OUTSIDE the dates' own isolates, so a range of
   * two dates and a dash has none, runs left to right, and an Arabic reader meets the end date first.
   */
  protected period(b: BookingListItem): string {
    return this.t('vehicleDetail.periodRange', {
      start: this.formats.date(b.periodStart),
      end: this.formats.date(b.periodEnd),
    });
  }

  /** The booking's total at its currency's own scale, with its own currency code. */
  protected total(b: BookingListItem): string {
    return this.formats.money(b.totalPrice, b.currency);
  }

  /**
   * Who has the car on this booking, for which dates, and how it changed hands — the clause after
   * the booking reference. A whole message per handover method: Arabic uses a different verb for a
   * car delivered and a car collected, not one word dropped into a shared sentence.
   */
  protected currentRentalLine(b: BookingListItem): string {
    return this.t(
      b.pickupMethod === 'Delivery'
        ? 'vehicleDetail.currentRentalDelivered'
        : 'vehicleDetail.currentRentalCollected',
      { customer: this.customerName(b), period: this.period(b) },
    );
  }

  /** The same clause for the next confirmed booking, which is still to hand over. */
  protected nextRentalLine(b: BookingListItem): string {
    return this.t(
      b.pickupMethod === 'Delivery'
        ? 'vehicleDetail.nextRentalDelivery'
        : 'vehicleDetail.nextRentalPickup',
      { customer: this.customerName(b), period: this.period(b) },
    );
  }

  /** "Approved by Rana Haddad": one change on the car's booking, and who made it. */
  protected logLine(e: DealerActivityEntry): string {
    return this.t('vehicleDetail.statusByActor', {
      status: this.statusLabel(e.toStatus, 'booking'),
      actor: this.actor(e),
    });
  }

  /**
   * Who made a change, as the API names them: a change recorded against no user is the rental
   * office's own, and a user with no name is somebody whose account has since been closed.
   */
  private actor(e: DealerActivityEntry): string {
    if (e.actorUserId === null) return this.t('common.theRentalOffice');
    return e.actorName ?? this.t('common.formerStaffMember');
  }

  /** The gearbox, in the reader's language. */
  private transmissionName(car: Vehicle): string {
    return this.vocabularyName(TRANSMISSIONS, car.transmission);
  }

  /** The fuel, in the reader's language. */
  private fuelName(car: Vehicle): string {
    return this.vocabularyName(FUEL_TYPES, car.fuelType);
  }

  /**
   * A server name worded through its table. A member this build does not know is spelled out and
   * isolated (U+2068 … U+2069, under Arabic) rather than left blank — the rule
   * `I18nService.enumLabel` follows for its families.
   */
  private vocabularyName(names: Readonly<Record<string, TranslationKey>>, name: string): string {
    if (!name) return '';
    if (Object.hasOwn(names, name)) return this.t(names[name]);
    const spelled = spellEnumName(name);
    return this.i18n.isRtl() ? `⁨${spelled}⁩` : spelled;
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
    if (c.status === 'Active')
      return { label: this.t('fleetList.hide'), action: 'Hide', icon: 'eye-slash' };
    if (c.status === 'Maintenance')
      return {
        label: this.t('vehicleDetail.backOnTheRoad'),
        action: 'ReturnFromMaintenance',
        icon: 'check-circle',
      };
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
      // (Vehicle.ReturnFromMaintenance), so it must not claim the car is bookable again. A toast
      // is worded as it is shown: it is gone before anyone could switch language under it.
      const told: Record<VehicleStatusAction, readonly [string, string]> = {
        Publish: [this.statusLabel('Active', 'vehicle'), this.t('vehicleDetail.customersCanSeeIt')],
        Hide: [this.statusLabel('Hidden', 'vehicle'), this.t('vehicleDetail.customersNoLongerSee')],
        SendToMaintenance: [this.t('status.offTheRoad'), this.t('vehicleDetail.itIsNotOffered')],
        ReturnFromMaintenance: [
          this.t('vehicleDetail.backOnTheRoadDone'),
          this.t('vehicleDetail.itIsHiddenUntil'),
        ],
      };
      const [title, body] = told[action];
      this.ui.showToast(title, body);
    } catch (error) {
      // From the refusal's facts: the server's own sentence only in English.
      const problem = snapshotProblem(error);
      this.ui.showToast(
        this.t('vehicleDetail.thatDidNotGo'),
        problem.code === 'vehicle.no_photos'
          ? this.t('vehicleDetail.addAtLeastOne')
          : (serverSentence(problem, this.i18n.lang(), this.t) ??
              this.t('vehicleDetail.theServiceDidNot')),
        'bad',
      );
    } finally {
      this.busy.set(false);
    }
  }

  protected takeOffRoad(): void {
    const c = this.car();
    if (!c) return;
    // Make, model and year are the car's own, never translated.
    const vehicle = `${c.make} ${c.model} ${c.year}`;
    this.ui.openAction(
      {
        icon: 'gear',
        tone: 'warn',
        title: this.t('fleetList.takeVehicleOffTheRoad', { vehicle }),
        body: this.t('vehicleDetail.everyDayShowsAs'),
        confirm: this.t('fleetList.takeOffTheRoad'),
        result: {
          title: this.t('vehicleDetail.offTheRoad'),
          body: this.t('fleetList.vehicleNotBeingOffered', { vehicle }),
          tone: 'warn',
        },
      },
      async () => {
        await this.service.changeStatus(c.vehicleId, 'SendToMaintenance');
        this.resource.reload();
        this.service.refresh();
      },
      {
        title: this.t('vehicleDetail.offTheRoad'),
        body: this.t('fleetList.vehicleNotBeingOffered', { vehicle }),
        tone: 'warn',
      },
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
