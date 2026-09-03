import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FleetService } from '../../core/services/fleet.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { Tone } from '../../core/models/console.models';
import { Vehicle, VehicleStatusAction } from '../../core/models/fleet.api';
import { IconComponent } from '../../shared/icon/icon.component';

/**
 * A dealer's own cars (spec 4.3).
 *
 * The first dealer-facing screen in the console. It shows STATUS and BOOKABLE separately, because a
 * car can be Active and still not reach customers — a suspended dealer's listings, for instance —
 * and quietly showing "live" in that case would be a lie the dealer only discovers from silence.
 */
@Component({
  selector: 'kh-fleet-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './fleet-list.component.html',
  imports: [RouterLink, IconComponent],
})
export class FleetListComponent {
  private readonly service = inject(FleetService);
  private readonly ui = inject(ConsoleUiService);
  private readonly router = inject(Router);

  protected readonly resource = this.service.vehicles;
  protected readonly busy = signal<string | null>(null);

  protected readonly cars = computed(() => this.resource.value() ?? []);

  protected readonly summary = computed(() => {
    const cars = this.cars();
    if (cars.length === 0) return '';
    const live = cars.filter((car) => car.isBookable).length;
    return `${cars.length} ${cars.length === 1 ? 'car' : 'cars'} · ${live} visible to customers`;
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

  protected cover(car: Vehicle): string | null {
    return car.images.find((image) => image.isPrimary)?.url ?? car.images[0]?.url ?? null;
  }

  protected statusTone(car: Vehicle): Tone {
    switch (car.status) {
      case 'Active':
        return car.isBookable ? 'ok' : 'warn';
      case 'Maintenance':
        return 'bad';
      case 'Draft':
        return 'accent';
      default:
        return 'dim';
    }
  }

  /** Says what the state MEANS, not just what it is called. */
  protected statusNote(car: Vehicle): string {
    if (car.status === 'Draft') return 'Not published yet';
    if (car.status === 'Maintenance') return 'Off the road';
    if (car.status === 'Hidden') return 'Not shown to customers';
    return car.isBookable ? 'Visible to customers' : 'Blocked — your dealership cannot trade';
  }

  protected primaryAction(car: Vehicle): { label: string; action: VehicleStatusAction } | null {
    if (car.status === 'Active') return { label: 'Hide', action: 'Hide' };
    if (car.status === 'Maintenance')
      return { label: 'Back from garage', action: 'ReturnFromMaintenance' };
    return { label: 'Publish', action: 'Publish' };
  }

  protected async changeStatus(car: Vehicle, action: VehicleStatusAction): Promise<void> {
    this.busy.set(car.vehicleId);
    try {
      await this.service.changeStatus(car.vehicleId, action);
      this.service.refresh();
    } catch (error) {
      const problem = error as { error?: { title?: string } };
      this.ui.showToast(
        'That did not go through',
        problem.error?.title ?? 'The service did not respond.',
        'bad',
      );
    } finally {
      this.busy.set(null);
    }
  }

  protected sendToMaintenance(car: Vehicle): void {
    void this.changeStatus(car, 'SendToMaintenance');
  }

  /** Deleting a listing is destructive from the dealer's side, so it states the consequence first. */
  protected remove(car: Vehicle): void {
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

  protected open(car: Vehicle): void {
    void this.router.navigate(['/fleet', car.vehicleId]);
  }

  protected reload(): void {
    this.resource.reload();
  }
}
