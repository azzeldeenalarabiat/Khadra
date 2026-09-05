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

type StateFilter = 'all' | 'Active' | 'Hidden' | 'Maintenance' | 'Draft';

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
  imports: [RouterLink, IconComponent, ImageFallbackDirective],
})
export class FleetListComponent {
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

  protected readonly states: readonly { key: StateFilter; label: string }[] = [
    { key: 'all', label: 'All' },
    { key: 'Active', label: 'Listed' },
    { key: 'Hidden', label: 'Hidden' },
    { key: 'Maintenance', label: 'Off the road' },
    { key: 'Draft', label: 'Draft' },
  ];

  protected readonly cars = computed(() => this.data() ?? []);
  /** Adding a car is the owner's (the API's ApprovedDealer policy); staff manage what exists. */
  protected readonly canAdd = computed(() => !!this.dealer()?.isOwner);

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
      { k: 'In your fleet', v: cars.length },
      { k: 'Listed', v: listed.length },
      {
        k: 'Available now',
        v: listed.filter((c) => c.isBookable && !hired.has(c.vehicleId)).length,
      },
      { k: 'On hire', v: cars.filter((c) => hired.has(c.vehicleId)).length },
      { k: 'Off the road', v: cars.filter((c) => c.status === 'Maintenance').length },
    ];
  });

  protected readonly failure = computed(() => {
    const error = this.resource.error() as
      { status?: number; error?: { code?: string } } | undefined;
    if (!error) return null;
    if (error.error?.code === 'dealer.not_registered') {
      return 'You have not submitted a dealer application yet, so there is no fleet to manage.';
    }
    if (error.status === 403) return 'Only dealer staff can manage a fleet.';
    return 'Your fleet could not be loaded. Nothing has been changed.';
  });

  protected isOnHire(car: Vehicle): boolean {
    return this.hiredVehicleIds().has(car.vehicleId);
  }

  protected cover(car: Vehicle): string | null {
    return car.images.find((image) => image.isPrimary)?.url ?? car.images[0]?.url ?? null;
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
    if (this.isOnHire(car)) return 'On hire';
    if (car.status === 'Draft') return 'Draft';
    if (car.status === 'Maintenance') return 'Off the road';
    if (car.status === 'Hidden') return 'Hidden';
    return car.isBookable ? 'Listed' : 'Blocked';
  }

  /** Says what the state MEANS, not just what it is called. */
  protected statusNote(car: Vehicle): string {
    if (this.isOnHire(car)) return 'Out with a customer';
    if (car.status === 'Draft') return 'Not published yet';
    if (car.status === 'Maintenance') return 'Not offered until it is back';
    if (car.status === 'Hidden') return 'Not shown to customers';
    return car.isBookable ? 'Visible to customers' : 'Your dealership cannot trade';
  }

  protected primaryAction(car: Vehicle): { label: string; action: VehicleStatusAction } | null {
    if (car.status === 'Active') return { label: 'Hide', action: 'Hide' };
    if (car.status === 'Maintenance')
      return { label: 'Back on the road', action: 'ReturnFromMaintenance' };
    return { label: 'Publish', action: 'Publish' };
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
      const problem = error as { error?: { code?: string; title?: string } };
      this.ui.showToast(
        'That did not go through',
        problem.error?.code === 'vehicle.no_photos'
          ? 'Add at least one photo before publishing.'
          : (problem.error?.title ?? 'The service did not respond.'),
        'bad',
      );
    } finally {
      this.busy.set(null);
    }
  }

  protected takeOffRoad(event: Event, car: Vehicle): void {
    event.stopPropagation();
    this.ui.openAction(
      {
        icon: 'gear',
        tone: 'warn',
        title: `Take ${car.make} ${car.model} off the road?`,
        body: 'It stops being offered to customers until you bring it back. Bookings already approved on it are not affected — tell those customers yourself if the car will not be ready.',
        confirm: 'Take off the road',
        result: {
          title: 'Off the road',
          body: `${car.make} ${car.model} is not being offered.`,
          tone: 'warn',
        },
      },
      async () => {
        await this.service.changeStatus(car.vehicleId, 'SendToMaintenance');
        this.service.refresh();
      },
      {
        title: 'Off the road',
        body: `${car.make} ${car.model} is not being offered.`,
        tone: 'warn',
      },
    );
  }

  /** Deleting a listing is destructive from the dealer's side, so it states the consequence first. */
  protected remove(event: Event, car: Vehicle): void {
    event.stopPropagation();
    this.ui.openAction(
      {
        icon: 'x-circle',
        tone: 'bad',
        danger: true,
        title: `Remove ${car.make} ${car.model}?`,
        body: 'It disappears from your fleet and from customer search. Bookings already made against it keep their history.',
        confirm: 'Remove car',
        result: { title: 'Car removed', body: '', tone: 'bad' },
      },
      async () => {
        await this.service.remove(car.vehicleId);
        this.service.refresh();
      },
      { title: 'Car removed', body: `${car.make} ${car.model} is no longer listed.`, tone: 'bad' },
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
