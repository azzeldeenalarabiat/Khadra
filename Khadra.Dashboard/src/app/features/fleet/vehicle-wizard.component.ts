import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { KeyValue } from '../../core/models/console.models';
import { Vehicle, VehicleRequest, toVehicleRequest } from '../../core/models/fleet.api';
import { FleetService } from '../../core/services/fleet.service';
import { DealerConsoleService } from '../../core/services/dealer-console.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { ImageFallbackDirective } from '../../shared/image-fallback.directive';
import { IconName } from '../../shared/icon/icon-paths';

interface Step {
  readonly n: number;
  readonly title: string;
  readonly icon: IconName;
}

/**
 * Add a vehicle in eight steps (design `isWizard`).
 *
 * The car becomes a real Draft on the platform when the dealer reaches Photos (step 5), because a
 * photo needs a car to attach to. Everything after that edits the draft; nothing is published until
 * the dealer chooses so on the last step. Leaving early keeps the draft in the fleet, where it is
 * visible as "Draft" and can be finished or removed.
 *
 * What the design shows and the platform decides differently: the pickup location is the
 * dealership's (spec 4.1, one location per dealer), so step 4 shows it read-only; "block specific
 * dates" is served by taking a car off the road, so it is not offered here; the delivery fee is the
 * platform's and is shown as a fact.
 */
@Component({
  selector: 'kh-vehicle-wizard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './vehicle-wizard.component.html',
  imports: [RouterLink, IconComponent, ImageFallbackDirective],
})
export class VehicleWizardComponent {
  private readonly service = inject(FleetService);
  private readonly consoleData = inject(DealerConsoleService);
  private readonly ui = inject(ConsoleUiService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly steps: readonly Step[] = [
    { n: 1, title: 'Basic information', icon: 'info' },
    { n: 2, title: 'Specifications', icon: 'gear' },
    { n: 3, title: 'Pricing', icon: 'currency-circle-dollar' },
    { n: 4, title: 'Location', icon: 'map-pin' },
    { n: 5, title: 'Photos', icon: 'image' },
    { n: 6, title: 'Availability', icon: 'toggle-right' },
    { n: 7, title: 'Delivery', icon: 'moped' },
    { n: 8, title: 'Review', icon: 'check-square' },
  ];

  protected readonly step = signal(1);
  protected readonly busy = signal(false);
  protected readonly uploading = signal(false);
  protected readonly problem = signal<string | null>(null);
  protected readonly fieldErrors = signal<Readonly<Record<string, readonly string[]>>>({});
  protected readonly draft = signal<Vehicle | null>(null);
  protected readonly publishOnSave = signal(true);
  /** A draft of the dealer's own that owns the plate they just typed; offered as "continue that". */
  protected readonly resumable = signal<Vehicle | null>(null);

  constructor() {
    // `?draft=<id>` means this wizard is continuing a draft created earlier (a refresh, or a return
    // from the fleet). The form is rebuilt from the car itself, never from anything cached here.
    const draftId = this.route.snapshot.queryParamMap.get('draft');
    if (draftId) this.service.editing.set(draftId);
    effect(() => {
      const existing = this.service.vehicle.value();
      if (draftId && existing && existing.vehicleId === draftId && !this.draft())
        this.hydrate(existing);
    });
  }

  protected readonly makes = [
    'Toyota',
    'Hyundai',
    'Kia',
    'Nissan',
    'Mitsubishi',
    'Chevrolet',
    'Honda',
    'Mercedes-Benz',
    'BMW',
  ];
  protected readonly transmissions = ['Automatic', 'Manual'];
  protected readonly fuelTypes = ['Petrol', 'Diesel', 'Hybrid', 'Electric'];
  protected readonly seatOptions = [2, 4, 5, 7, 8];
  protected readonly years = Array.from({ length: 12 }, (_, i) => new Date().getFullYear() + 1 - i);

  protected readonly form = signal<VehicleRequest>({
    carTypeId: '01a06675-0000-7000-8000-000000000001',
    make: 'Toyota',
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

  protected readonly me = this.consoleData.me;
  protected readonly delivery = this.consoleData.delivery;
  protected readonly current = computed(() => this.steps[this.step() - 1]);
  protected readonly photos = computed(() => this.draft()?.images ?? []);

  protected readonly stepValid = computed(() => {
    const f = this.form();
    switch (this.step()) {
      case 1:
        return (
          f.make.trim().length > 0 &&
          f.model.trim().length > 0 &&
          f.year >= 1990 &&
          f.year <= new Date().getFullYear() + 1
        );
      case 2:
        return f.plateNumber.trim().length > 0 && f.seats > 0;
      case 3:
        return (
          f.dailyRate > 0 &&
          f.securityDeposit >= 0 &&
          (f.mileageUnlimited ||
            ((f.mileageDailyLimitKm ?? 0) > 0 && (f.mileageExcessFeePerKm ?? 0) >= 0))
        );
      default:
        return true;
    }
  });

  protected readonly review = computed<readonly KeyValue[]>(() => {
    const f = this.form();
    const me = this.me.value();
    const fee = this.delivery.value()?.platformDeliveryFee;
    return [
      { k: 'Plate', v: f.plateNumber || '—' },
      { k: 'Colour', v: f.color || '—' },
      {
        k: 'Location',
        v: me ? `${me.businessName} · ${me.latitude.toFixed(4)}, ${me.longitude.toFixed(4)}` : '—',
      },
      { k: 'Listing', v: this.publishOnSave() ? 'Published on save' : 'Kept as a draft' },
      {
        k: 'Mileage',
        v: f.mileageUnlimited
          ? 'Unlimited'
          : `${f.mileageDailyLimitKm} km/day · ${f.mileageExcessFeePerKm} JOD/km over`,
      },
      { k: 'Fuel policy', v: f.fuelPolicy === 'FullToFull' ? 'Full to full' : 'Same to same' },
      {
        k: 'Delivery',
        v: f.isDeliveryEligible
          ? `Eligible${fee ? ` · fee ${fee.amount} ${fee.currency} (platform-wide)` : ''}`
          : 'Pickup only',
      },
      { k: 'Photos', v: `${this.photos().length} uploaded` },
      { k: 'Insurance', v: 'Pending platform configuration', tone: 'dim' },
    ];
  });

  protected readonly footer = computed(() => {
    if (this.step() === 8) return 'Nothing is published until you save.';
    return this.draft()
      ? 'Saved as a draft in your fleet'
      : 'Becomes a draft once you reach Photos';
  });

  protected patch(patch: Partial<VehicleRequest>): void {
    this.form.update((f) => ({ ...f, ...patch }));
  }

  protected text(event: Event): string {
    return (event.target as HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement).value;
  }

  protected number(event: Event): number {
    return Number((event.target as HTMLInputElement).value);
  }

  protected fieldError(name: string): string | null {
    const errors = this.fieldErrors();
    const key = Object.keys(errors).find((k) => k.toLowerCase() === name.toLowerCase());
    return key ? (errors[key][0] ?? null) : null;
  }

  protected goTo(n: number): void {
    // Forward jumps go through the same gate as Continue; backward ones are always fine.
    if (n <= this.step() || (n === this.step() + 1 && this.stepValid())) void this.advanceTo(n);
  }

  protected back(): void {
    this.step.update((s) => Math.max(1, s - 1));
  }

  protected async next(): Promise<void> {
    if (!this.stepValid() || this.busy()) return;
    if (this.step() === 8) {
      await this.save();
      return;
    }
    await this.advanceTo(this.step() + 1);
  }

  private async advanceTo(n: number): Promise<void> {
    // Reaching Photos needs a car on the platform to attach them to; reaching it AGAIN saves
    // whatever was changed in steps 1-4 since, so the draft in the fleet never lags the form.
    if (n === 5 || (n > 5 && !this.draft())) {
      const saved = await this.persist();
      if (!saved) return;
    }
    this.problem.set(null);
    this.step.set(n);
  }

  /** Creates the draft the first time, updates it after. Returns false when the API refused. */
  private async persist(): Promise<boolean> {
    this.busy.set(true);
    this.problem.set(null);
    this.fieldErrors.set({});
    try {
      const draft = this.draft();
      const saved = draft
        ? await this.service.update(draft.vehicleId, this.form())
        : await this.service.add(this.form());
      this.draft.set(saved);
      this.service.refresh();
      // The draft's id goes into the URL, so a refresh or a wrong turn brings the dealer back to
      // THIS draft instead of leaving it orphaned in the fleet and its plate "taken".
      if (!draft) {
        await this.router.navigate([], {
          queryParams: { draft: saved.vehicleId },
          replaceUrl: true,
        });
      }
      return true;
    } catch (error) {
      const p = error as {
        error?: { code?: string; title?: string; errors?: Record<string, string[]> };
      };
      if (p.error?.errors) this.fieldErrors.set(p.error.errors);
      this.problem.set(
        p.error?.code === 'vehicle.plate_taken'
          ? this.plateTakenMessage()
          : (p.error?.title ?? 'The service did not respond. Nothing has been changed.'),
      );
      // Field errors belong to the early steps; go back to the first one that can show them.
      if (p.error?.errors) this.step.set(1);
      if (p.error?.code === 'vehicle.plate_taken') this.step.set(2);
      return false;
    } finally {
      this.busy.set(false);
    }
  }

  protected async upload(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    const draft = this.draft();
    if (files.length === 0 || !draft || this.uploading()) return;
    this.uploading.set(true);
    this.problem.set(null);
    try {
      for (const file of files) {
        this.draft.set(await this.service.uploadImage(draft.vehicleId, file));
      }
    } catch (error) {
      const p = error as { error?: { code?: string; title?: string } };
      this.problem.set(
        p.error?.code === 'vehicle.invalid_image_type'
          ? 'Use a JPEG, PNG or WebP image.'
          : (p.error?.title ?? 'The upload did not go through.'),
      );
    } finally {
      this.uploading.set(false);
      input.value = '';
    }
  }

  protected async removePhoto(imageId: string): Promise<void> {
    const draft = this.draft();
    if (!draft || this.uploading()) return;
    this.uploading.set(true);
    try {
      this.draft.set(await this.service.removeImage(draft.vehicleId, imageId));
    } finally {
      this.uploading.set(false);
    }
  }

  protected async setCover(imageId: string): Promise<void> {
    const draft = this.draft();
    if (!draft || this.uploading()) return;
    this.uploading.set(true);
    try {
      this.draft.set(await this.service.setPrimaryImage(draft.vehicleId, imageId));
    } finally {
      this.uploading.set(false);
    }
  }

  private async save(): Promise<void> {
    if (!(await this.persist())) return;
    const draft = this.draft();
    if (!draft) return;
    if (this.publishOnSave()) {
      if (this.photos().length === 0) {
        this.problem.set('Add at least one photo before publishing, or save it as a draft.');
        this.step.set(5);
        return;
      }
      this.busy.set(true);
      try {
        await this.service.changeStatus(draft.vehicleId, 'Publish');
        this.service.refresh();
      } catch (error) {
        const p = error as { error?: { code?: string; title?: string } };
        this.problem.set(
          p.error?.code === 'dealer.not_approved'
            ? 'Your dealership cannot trade right now, so the car was saved as a draft instead of published.'
            : (p.error?.title ?? 'The car was saved as a draft; publishing did not go through.'),
        );
        this.busy.set(false);
        return;
      } finally {
        this.busy.set(false);
      }
    }
    const f = this.form();
    this.ui.showToast(
      this.publishOnSave() ? 'Vehicle published' : 'Draft saved',
      this.publishOnSave()
        ? `${f.make} ${f.model} ${f.year} is live in your fleet.`
        : `${f.make} ${f.model} ${f.year} is in your fleet as a draft.`,
    );
    await this.router.navigate(['/dealer/fleet', draft.vehicleId]);
  }

  protected async cancel(): Promise<void> {
    const draft = this.draft();
    if (draft) {
      // Leaving keeps the draft, so the draft must hold what the form holds.
      await this.persist();
      this.ui.showToast(
        'Draft kept',
        `${draft.make} ${draft.model} stays in your fleet as a draft. Open it from the fleet to finish.`,
        'warn',
      );
    }
    await this.router.navigate(['/dealer/fleet']);
  }

  /** A "taken" plate is very often the dealer's own abandoned draft; say so and point at it. */
  private plateTakenMessage(): string {
    const plate = this.form().plateNumber.trim();
    const own = (this.service.vehicles.value() ?? []).find((car) => car.plateNumber === plate);
    if (own?.status === 'Draft') {
      this.resumable.set(own);
      return `${plate} is on a draft you already started (${own.make} ${own.model} ${own.year}). Continue that draft instead of creating another.`;
    }
    return own
      ? `${plate} is already on ${own.make} ${own.model} ${own.year} in your fleet.`
      : 'That plate is already on another car on the platform.';
  }

  /** Switch this wizard onto an existing draft: same URL state as a refresh would produce. */
  protected async resume(existing: Vehicle): Promise<void> {
    await this.router.navigate([], {
      queryParams: { draft: existing.vehicleId },
      replaceUrl: true,
    });
    this.resumable.set(null);
    this.hydrate(existing);
  }

  private hydrate(existing: Vehicle): void {
    this.draft.set(existing);
    this.form.set(toVehicleRequest(existing));
    this.problem.set(null);
    this.fieldErrors.set({});
    this.step.set(5);
  }
}
