import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FleetService } from '../../core/services/fleet.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { Vehicle, VehicleRequest } from '../../core/models/fleet.api';
import { IconComponent } from '../../shared/icon/icon.component';

/**
 * Add or edit one car (spec 4.3).
 *
 * One component for both, because the fields are identical and the only real difference is whether
 * photos can be attached yet: an upload needs a car to attach to, so the image manager appears once
 * the car exists. Splitting this into two screens would duplicate every field to express that.
 */
@Component({
  selector: 'kh-car-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './car-form.component.html',
  imports: [FormsModule, RouterLink, IconComponent],
})
export class CarFormComponent {
  private readonly service = inject(FleetService);
  private readonly ui = inject(ConsoleUiService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly vehicleId = this.route.snapshot.paramMap.get('vehicleId');
  protected readonly resource = this.service.vehicle;
  protected readonly saving = signal(false);
  protected readonly uploading = signal(false);
  protected readonly problem = signal<string | null>(null);

  protected readonly transmissions = ['Automatic', 'Manual'];
  protected readonly fuelTypes = ['Petrol', 'Diesel', 'Hybrid', 'Electric'];
  protected readonly fuelPolicies = ['FullToFull', 'SameToSame'];

  // The form's own state. Seeded from the server when editing, defaulted when adding.
  protected readonly form = signal<VehicleRequest>({
    carTypeId: '01a06675-0000-7000-8000-000000000001',
    make: '',
    model: '',
    year: new Date().getFullYear(),
    color: null,
    seats: 5,
    transmission: 'Automatic',
    fuelType: 'Petrol',
    description: null,
    plateNumber: '',
    dailyRate: 30,
    securityDeposit: 150,
    isDeliveryEligible: false,
    mileageUnlimited: true,
    mileageDailyLimitKm: null,
    mileageExcessFeePerKm: null,
    fuelPolicy: 'FullToFull',
  });

  constructor() {
    this.service.editing.set(this.vehicleId);
    effect(() => {
      const car = this.resource.value();
      if (car) this.form.set(toRequest(car));
    });
  }

  protected readonly car = computed(() => this.resource.value() ?? null);
  protected readonly isEditing = computed(() => this.vehicleId !== null);
  protected readonly images = computed(() => this.car()?.images ?? []);

  /** Publishing needs a photo, so the form says so before the dealer discovers it as an error. */
  protected readonly publishHint = computed(() => {
    const car = this.car();
    if (!car) return null;
    if (car.images.length === 0) return 'Add at least one photo before you can publish this car.';
    if (car.status === 'Draft')
      return 'This car is a draft. Publish it from your fleet when you are ready.';
    return null;
  });

  protected set<K extends keyof VehicleRequest>(key: K, value: VehicleRequest[K]): void {
    this.form.update((current) => ({ ...current, [key]: value }));
  }

  protected text(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement).value;
  }

  protected numeric(event: Event): number {
    return Number((event.target as HTMLInputElement).value);
  }

  protected checked(event: Event): boolean {
    return (event.target as HTMLInputElement).checked;
  }

  protected async save(): Promise<void> {
    if (this.saving()) return;
    this.saving.set(true);
    this.problem.set(null);

    try {
      const body = this.form();
      const saved = this.vehicleId
        ? await this.service.update(this.vehicleId, body)
        : await this.service.add(body);

      this.service.refresh();
      this.ui.showToast(
        this.vehicleId ? 'Car updated' : 'Car added',
        `${saved.year} ${saved.make} ${saved.model} is saved as ${saved.status}.`,
      );

      // A new car goes straight to its own page so photos can be attached; there is nowhere to
      // upload them until the car exists.
      if (!this.vehicleId) await this.router.navigate(['/fleet', saved.vehicleId]);
    } catch (error) {
      this.problem.set(describe(error));
    } finally {
      this.saving.set(false);
    }
  }

  protected async upload(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file || !this.vehicleId) return;

    this.uploading.set(true);
    this.problem.set(null);
    try {
      await this.service.uploadImage(this.vehicleId, file);
      this.service.refresh();
    } catch (error) {
      this.problem.set(describe(error));
    } finally {
      this.uploading.set(false);
      // Clear it so choosing the same file again still fires a change event.
      input.value = '';
    }
  }

  protected async removeImage(imageId: string): Promise<void> {
    if (!this.vehicleId) return;
    try {
      await this.service.removeImage(this.vehicleId, imageId);
      this.service.refresh();
    } catch (error) {
      this.problem.set(describe(error));
    }
  }

  protected async makePrimary(imageId: string): Promise<void> {
    if (!this.vehicleId) return;
    try {
      await this.service.setPrimaryImage(this.vehicleId, imageId);
      this.service.refresh();
    } catch (error) {
      this.problem.set(describe(error));
    }
  }
}

function toRequest(car: Vehicle): VehicleRequest {
  return {
    carTypeId: car.carTypeId,
    make: car.make,
    model: car.model,
    year: car.year,
    color: car.color,
    seats: car.seats,
    transmission: car.transmission,
    fuelType: car.fuelType,
    description: car.description,
    plateNumber: car.plateNumber,
    dailyRate: car.dailyRate.amount,
    securityDeposit: car.securityDeposit.amount,
    isDeliveryEligible: car.isDeliveryEligible,
    mileageUnlimited: car.mileage.isUnlimited,
    mileageDailyLimitKm: car.mileage.dailyLimitKm,
    mileageExcessFeePerKm: car.mileage.excessFeePerKm?.amount ?? null,
    fuelPolicy: car.fuelPolicy,
  };
}

function describe(error: unknown): string {
  const problem = error as { status?: number; error?: { code?: string; title?: string } };
  if (problem.error?.code === 'dealer.not_approved') {
    return 'Your dealership is not approved yet, so you cannot manage cars. You will be able to once an administrator approves your application.';
  }
  if (problem.error?.code === 'vehicle.plate_taken') {
    return 'A car with that plate number is already listed on the platform.';
  }
  return problem.error?.title ?? 'The service did not respond. Nothing has been saved.';
}
