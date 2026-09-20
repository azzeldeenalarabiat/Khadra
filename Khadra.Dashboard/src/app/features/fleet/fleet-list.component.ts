import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { Router, RouterLink } from '@angular/router';
import { FleetService } from '../../core/services/fleet.service';
import { DealerConsoleService } from '../../core/services/dealer-console.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { Tone } from '../../core/models/console.models';
import { BookingListItem, PagedResult } from '../../core/models/bookings.api';
import { Vehicle, VehicleStatusAction } from '../../core/models/fleet.api';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { ImageFallbackDirective } from '../../shared/image-fallback.directive';
import { TranslationKey } from '../../core/i18n/en';
import { I18nService } from '../../core/i18n/i18n.service';
import { serverSentence, snapshotProblem } from '../../core/i18n/problem';
import { spellEnumName } from '../../core/i18n/status-key';
import { MoneyPipe } from '../../shared/money.pipe';

type StateFilter = 'all' | 'Active' | 'Hidden' | 'Maintenance' | 'Draft';

/**
 * The state chips: the car status each one filters on, and the key that words it.
 *
 * The value is compared with `car.status`, so it stays the server's name; the words are chosen when
 * the chips render. They were a field initialiser once, which resolved at construction and left the
 * chips in the old language after a switch — and only one of the five was keyed at all, which is how
 * a row reading "All | Listed | Hidden | مسحوبة من الخدمة | Draft" happened.
 */
const STATES: readonly { readonly key: StateFilter; readonly label: TranslationKey }[] = [
  { key: 'all', label: 'fleetList.all' },
  { key: 'Active', label: 'fleetList.listed' },
  { key: 'Hidden', label: 'fleetList.hidden' },
  { key: 'Maintenance', label: 'fleetList.offTheRoad' },
  { key: 'Draft', label: 'fleetList.draft' },
];

/**
 * The server's transmission and fuel names, and the keys that word them.
 *
 * The name is what the car carries and what the form sends back; the words are the reader's. The
 * Arabic is the wording `/api/v1/app-config` publishes for the customer app, so a customer and the
 * rental office call a gearbox the same thing. `vehicle-detail` words the same two sets.
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
 * A dealer's own cars (spec 4.3, design `isList` for fleet).
 *
 * STATUS is what the dealer set; BOOKABLE is the server's answer to "would a customer see this",
 * which also depends on the dealership's own standing. Both are shown, because a car can be Active
 * and still unreachable — a suspended dealer's listings, say — and calling that "live" would be a lie
 * the dealer only discovers from silence.
 *
 * "On hire" is derived from bookings (a car that is PickedUp right now), never from a flag on the car:
 * availability is a question about bookings, and there is no "Booked" status to drift out of date.
 */
@Component({
  selector: 'kh-fleet-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './fleet-list.component.html',
  imports: [RouterLink, IconComponent, ImageFallbackDirective, MoneyPipe],
})
export class FleetListComponent {
  private readonly i18n = inject(I18nService);
  protected readonly t = this.i18n.t;
  private readonly service = inject(FleetService);
  private readonly ui = inject(ConsoleUiService);
  private readonly router = inject(Router);
  private readonly consoleData = inject(DealerConsoleService);

  protected readonly resource = this.service.vehicles;
  /** Guarded: `value()` throws in the error state, so nothing reads the resource directly. */
  private readonly data = loaded(this.resource);
  protected readonly busy = signal<string | null>(null);
  protected readonly state = signal<StateFilter>('all');
  protected readonly search = signal('');

  /** Cars out on hire right now: the active tab is exactly the PickedUp bookings. */
  private readonly onHire = httpResource<PagedResult<BookingListItem>>(() => ({
    url: '/api/v1/bookings',
    params: { tab: 'active', page: 1, pageSize: 100 },
  }));
  private readonly hires = loaded(this.onHire);
  private readonly dealer = loaded(this.consoleData.me);

  /** The chips in the reader's language. A `computed`, not a field: a field words them only once. */
  protected readonly states = computed(() =>
    STATES.map((option) => ({ key: option.key, label: this.t(option.label) })),
  );

  protected readonly cars = computed(() => this.data() ?? []);

  /**
   * The fleet is the owner's to change; every member of staff may read it.
   *
   * `ApprovedDealer` sits on all ten writes in `DealerVehiclesController` — add, edit, publish,
   * hide, take off the road, remove, and every image call — while the two GETs take `DealerStaff`.
   * So an employee gets the whole screen and none of the buttons, and is told once, at the top, that
   * this is deliberate. Leaving them on screen but disabled was the other option and is worse here:
   * Edit is an anchor, which ignores `disabled`, and four dead controls repeated on every card is
   * noise rather than information.
   *
   * `null` until `me` answers, so an owner's own buttons never blink out and back in.
   */
  protected readonly canManage = computed(
    () => this.consoleData.permissions()?.canManageFleet ?? null,
  );
  protected readonly canAdd = computed(() => this.canManage() === true);
  /** Said only once it is known to be true; "read-only" is a claim, not a default. */
  protected readonly readOnly = computed(
    () => this.canManage() === false && !this.dealer()?.isOwner,
  );

  private readonly hiredVehicleIds = computed(() => {
    const items = this.hires()?.items ?? [];
    return new Set(items.map((b) => b.vehicle?.vehicleId).filter((id): id is string => !!id));
  });

  protected readonly rows = computed(() => {
    const term = this.search().trim().toLowerCase();
    const state = this.state();
    return this.cars().filter((car) => {
      if (state !== 'all' && car.status !== state) return false;
      if (!term) return true;
      return `${car.make} ${car.model} ${car.year} ${car.plateNumber} ${car.color ?? ''}`
        .toLowerCase()
        .includes(term);
    });
  });

  protected readonly stats = computed(() => {
    const cars = this.cars();
    const hired = this.hiredVehicleIds();
    const listed = cars.filter((c) => c.status === 'Active');
    return [
      { k: this.t('fleetList.inYourFleet'), v: cars.length },
      { k: this.t('fleetList.listed'), v: listed.length },
      {
        k: this.t('fleetList.availableNow'),
        v: listed.filter((c) => c.isBookable && !hired.has(c.vehicleId)).length,
      },
      { k: this.t('fleetList.onHire'), v: cars.filter((c) => hired.has(c.vehicleId)).length },
      { k: this.t('fleetList.offTheRoad'), v: cars.filter((c) => c.status === 'Maintenance').length },
    ];
  });

  /** Why the fleet did not load, held as facts and worded in `failure`, so a switch re-words it. */
  private readonly problem = computed(() => {
    const error = this.resource.error();
    return error ? snapshotProblem(error) : null;
  });

  protected readonly failure = computed(() => {
    const problem = this.problem();
    if (!problem) return null;
    if (problem.code === 'dealer.not_registered') {
      return this.t('fleetList.youHaveNotSubmitted');
    }
    if (problem.status === 403) return this.t('fleetList.onlyDealerStaffCan');
    return this.t('fleetList.yourFleetCouldNot');
  });

  protected isOnHire(car: Vehicle): boolean {
    return this.hiredVehicleIds().has(car.vehicleId);
  }

  protected cover(car: Vehicle): string | null {
    return car.images.find((image) => image.isPrimary)?.url ?? car.images[0]?.url ?? null;
  }

  /** The gearbox, in the reader's language. */
  protected transmissionName(car: Vehicle): string {
    return this.vocabularyName(TRANSMISSIONS, car.transmission);
  }

  /** The fuel, in the reader's language. */
  protected fuelName(car: Vehicle): string {
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

  /** A car as a dialog names it. Make, model and year are the car's own, never translated. */
  private vehicleName(car: Vehicle): string {
    return `${car.make} ${car.model} ${car.year}`;
  }

  protected statusTone(car: Vehicle): Tone {
    if (this.isOnHire(car)) return 'accent';
    switch (car.status) {
      case 'Active':
        return car.isBookable ? 'ok' : 'warn';
      case 'Maintenance':
        return 'bad';
      case 'Draft':
        return 'dim';
      default:
        return 'dim';
    }
  }

  protected statusLabel(car: Vehicle): string {
    if (this.isOnHire(car)) return this.t('fleetList.onHire');
    if (car.status === 'Draft') return this.t('fleetList.draft');
    if (car.status === 'Maintenance') return this.t('fleetList.offTheRoad');
    if (car.status === 'Hidden') return this.t('fleetList.hidden');
    return car.isBookable ? this.t('fleetList.listed') : this.t('fleetList.blocked');
  }

  /** Says what the state MEANS, not just what it is called. */
  protected statusNote(car: Vehicle): string {
    if (this.isOnHire(car)) return this.t('fleetList.outWithACustomer');
    if (car.status === 'Draft') return this.t('fleetList.notPublishedYet');
    if (car.status === 'Maintenance') return this.t('fleetList.notOfferedUntilBack');
    if (car.status === 'Hidden') return this.t('fleetList.notShownToCustomers');
    return car.isBookable ? this.t('fleetList.visibleToCustomers') : this.t('fleetList.cannotTrade');
  }

  protected primaryAction(car: Vehicle): { label: string; action: VehicleStatusAction } | null {
    if (car.status === 'Active') return { label: this.t('fleetList.hide'), action: 'Hide' };
    if (car.status === 'Maintenance')
      return { label: this.t('fleetList.backOnTheRoad'), action: 'ReturnFromMaintenance' };
    return { label: this.t('fleetList.publish'), action: 'Publish' };
  }

  protected setSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
  }

  protected async changeStatus(
    event: Event,
    car: Vehicle,
    action: VehicleStatusAction,
  ): Promise<void> {
    event.stopPropagation();
    this.busy.set(car.vehicleId);
    try {
      await this.service.changeStatus(car.vehicleId, action);
      this.service.refresh();
    } catch (error) {
      // Worded as it is shown, from the refusal's facts: the server's sentence only in English.
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
      this.busy.set(null);
    }
  }

  protected takeOffRoad(event: Event, car: Vehicle): void {
    event.stopPropagation();
    const vehicle = this.vehicleName(car);
    this.ui.openAction(
      {
        icon: 'gear',
        tone: 'warn',
        title: this.t('fleetList.takeVehicleOffTheRoad', { vehicle }),
        body: this.t('fleetList.itStopsBeingOffered'),
        confirm: this.t('fleetList.takeOffTheRoad'),
        result: {
          title: this.t('fleetList.offTheRoad'),
          body: this.t('fleetList.vehicleNotBeingOffered', { vehicle }),
          tone: 'warn',
        },
      },
      async () => {
        await this.service.changeStatus(car.vehicleId, 'SendToMaintenance');
        this.service.refresh();
      },
      {
        title: this.t('fleetList.offTheRoad'),
        body: this.t('fleetList.vehicleNotBeingOffered', { vehicle }),
        tone: 'warn',
      },
    );
  }

  /** Deleting a listing is destructive from the dealer's side, so it states the consequence first. */
  protected remove(event: Event, car: Vehicle): void {
    event.stopPropagation();
    const vehicle = this.vehicleName(car);
    this.ui.openAction(
      {
        icon: 'x-circle',
        tone: 'bad',
        danger: true,
        title: this.t('fleetList.removeVehicle', { vehicle }),
        body: this.t('fleetList.itDisappearsFromYour'),
        confirm: this.t('fleetList.removeCar'),
        result: { title: this.t('fleetList.carRemoved'), body: '', tone: 'bad' },
      },
      async () => {
        await this.service.remove(car.vehicleId);
        this.service.refresh();
      },
      {
        title: this.t('fleetList.carRemoved'),
        body: this.t('fleetList.vehicleNoLongerListed', { vehicle }),
        tone: 'bad',
      },
    );
  }

  // `open(car)` lived here for the table's whole-row click. The cards link to the car directly from
  // the photo and the name, so the row-click indirection — and the stopPropagation it forced on
  // every control inside it — is gone.

  protected reload(): void {
    this.resource.reload();
    this.onHire.reload();
  }
}
