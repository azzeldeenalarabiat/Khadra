import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { FleetService } from '../../core/services/fleet.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { Vehicle, VehicleRequest, toVehicleRequest } from '../../core/models/fleet.api';
import { LookupEntry, LookupsService } from '../../core/services/lookups.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/en';
import { Language } from '../../core/i18n/language';
import {
  ProblemSnapshot,
  fieldMessage,
  serverSentence,
  snapshotProblem,
} from '../../core/i18n/problem';

/**
 * The API's transmission and fuel names, each beside the key that words it — the same two tables the
 * fleet list and the car page use. The name is what the form sends and the server stores; only the
 * label is the reader's.
 */
const TRANSMISSION_LABELS: Readonly<Record<string, TranslationKey>> = {
  Automatic: 'fleetList.transmissionAutomatic',
  Manual: 'fleetList.transmissionManual',
};
const FUEL_TYPE_LABELS: Readonly<Record<string, TranslationKey>> = {
  Petrol: 'fleetList.fuelPetrol',
  Diesel: 'fleetList.fuelDiesel',
  Hybrid: 'fleetList.fuelHybrid',
  Electric: 'fleetList.fuelElectric',
};
/** The two fuel policies the API accepts, worded as the wizard and the car page word them. */
const FUEL_POLICY_LABELS: Readonly<Record<string, TranslationKey>> = {
  FullToFull: 'vehicleWizard.fullToFull',
  SameToSame: 'vehicleWizard.sameToSame',
};

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
  private readonly i18n = inject(I18nService);
  protected readonly t = this.i18n.t;
  // The server's own enum names, in the reader's language, from the shared table rather than a map
  // per screen: a copy each is a copy each to forget a new member in.
  private readonly statusLabel = this.i18n.statusLabel;
  private readonly service = inject(FleetService);
  private readonly ui = inject(ConsoleUiService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly vehicleId = toSignal(
    this.route.paramMap.pipe(map((params) => params.get('vehicleId'))),
    { initialValue: this.route.snapshot.paramMap.get('vehicleId') },
  );
  protected readonly resource = this.service.vehicle;
  /** Guarded: `value()` throws in the error state, so nothing reads the resource directly. */
  private readonly data = loaded(this.resource);
  protected readonly saving = signal(false);
  protected readonly uploading = signal(false);
  /** What went wrong, held as facts; `problemText` chooses the words when it is shown. */
  private readonly problem = signal<FormProblem | null>(null);
  protected readonly problemText = computed(() => {
    const problem = this.problem();
    return problem ? describe(problem, this.t, this.i18n.lang()) : null;
  });

  /** The API's names, in the order offered. Worded by the three label helpers below. */
  protected readonly transmissions = Object.keys(TRANSMISSION_LABELS);
  protected readonly fuelTypes = Object.keys(FUEL_TYPE_LABELS);
  protected readonly fuelPolicies = Object.keys(FUEL_POLICY_LABELS);

  /**
   * The vehicle types, from the platform's own list.
   *
   * This field used to be absent and the id below was a literal: every car saved from this form
   * claimed a type nobody had chosen and which nothing guaranteed existed. There is no foreign key
   * on `vehicles.car_type_id` to have caught it either.
   */
  private readonly lookups = inject(LookupsService);
  protected readonly carTypes = loaded(this.lookups.carTypes);
  protected readonly carTypesFailure = computed(() =>
    this.lookups.carTypes.error() ? this.t('vehicleWizard.vehicleTypesCouldNot') : null,
  );
  /** The platform's model-year bounds. This input had none at all, so it took anything. */
  protected readonly yearRange = loaded(this.lookups.modelYears);

  // The form's own state. Seeded from the server when editing, defaulted when adding.
  protected readonly form = signal<VehicleRequest>({
    carTypeId: '',
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
    effect(() => this.service.editing.set(this.vehicleId()));
    effect(() => {
      const car = this.data();
      if (car) this.form.set(toVehicleRequest(car));
    });
    // A `<select>` shows its first option whatever the model says, so an unset id would look chosen
    // and save as an empty string. Only fills a blank -- an existing car keeps the type it has.
    effect(() => {
      const first = this.carTypes()?.[0];
      if (first && !this.form().carTypeId) this.set('carTypeId', first.id);
    });
  }

  protected readonly car = computed(() => this.data() ?? null);
  protected readonly isEditing = computed(() => this.vehicleId() !== null);
  protected readonly images = computed(() => this.car()?.images ?? []);

  /** Publishing needs a photo, so the form says so before the dealer discovers it as an error. */
  protected readonly publishHint = computed(() => {
    const car = this.car();
    if (!car) return null;
    if (car.images.length === 0) return this.t('carForm.addAtLeastOne');
    if (car.status === 'Draft') return this.t('carForm.thisCarIsA');
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

  /**
   * What the server said about one field, under the same language rule as the banner: its own English
   * sentence in English, and a line of the console's own in Arabic.
   */
  protected fieldError(name: string): string | null {
    const problem = this.problem();
    return problem?.kind === 'request'
      ? fieldMessage(problem.snapshot, name, this.i18n.lang(), this.t)
      : null;
  }

  /** The gearbox as the reader's language says it. */
  protected transmissionLabel(name: string): string {
    return wordFor(TRANSMISSION_LABELS, name, this.t);
  }

  /** The fuel, the same way. */
  protected fuelTypeLabel(name: string): string {
    return wordFor(FUEL_TYPE_LABELS, name, this.t);
  }

  /** And the fuel policy, which is a choice rather than a fact about the car. */
  protected fuelPolicyLabel(name: string): string {
    return wordFor(FUEL_POLICY_LABELS, name, this.t);
  }

  /**
   * A vehicle type in the reader's language. Both names come from the same curated row and the id is
   * what is sent either way. Falls back to the other name rather than an empty option: a lookup row
   * may be half-translated, and an unnamed option cannot be picked.
   */
  protected typeName(type: LookupEntry): string {
    const arabic = this.i18n.lang() === 'ar';
    return (arabic ? type.nameAr || type.nameEn : type.nameEn || type.nameAr).trim();
  }

  protected async save(): Promise<void> {
    if (this.saving()) return;
    if (!this.form().carTypeId) {
      this.problem.set({ kind: 'noType' });
      return;
    }
    this.saving.set(true);
    this.problem.set(null);

    try {
      const body = this.form();
      const id = this.vehicleId();
      const saved = id ? await this.service.update(id, body) : await this.service.add(body);

      this.service.refresh();
      // A toast is worded as it is shown: it is gone before anyone could switch language under it.
      // Year, make and model are the car's own and are never translated; the status is.
      this.ui.showToast(
        id ? this.t('carForm.carUpdated') : this.t('carForm.carAdded'),
        this.t('carForm.savedAsStatus', {
          vehicle: `${saved.year} ${saved.make} ${saved.model}`,
          status: this.statusLabel(saved.status, 'vehicle'),
        }),
      );

      // A new car goes straight to its own page so photos can be attached; there is nowhere to
      // upload them until the car exists.
      if (!id) await this.router.navigate(['/dealer/fleet', saved.vehicleId]);
    } catch (error) {
      this.problem.set({ kind: 'request', snapshot: snapshotProblem(error) });
    } finally {
      this.saving.set(false);
    }
  }

  protected async upload(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    const id = this.vehicleId();
    if (!file || !id) return;

    this.uploading.set(true);
    this.problem.set(null);
    try {
      await this.service.uploadImage(id, file);
      this.service.refresh();
    } catch (error) {
      this.problem.set({ kind: 'request', snapshot: snapshotProblem(error) });
    } finally {
      this.uploading.set(false);
      // Clear it so choosing the same file again still fires a change event.
      input.value = '';
    }
  }

  protected async removeImage(imageId: string): Promise<void> {
    const id = this.vehicleId();
    if (!id) return;
    try {
      await this.service.removeImage(id, imageId);
      this.service.refresh();
    } catch (error) {
      this.problem.set({ kind: 'request', snapshot: snapshotProblem(error) });
    }
  }

  protected async makePrimary(imageId: string): Promise<void> {
    const id = this.vehicleId();
    if (!id) return;
    try {
      await this.service.setPrimaryImage(id, imageId);
      this.service.refresh();
    } catch (error) {
      this.problem.set({ kind: 'request', snapshot: snapshotProblem(error) });
    }
  }
}

/**
 * What the banner reports, as facts rather than as a sentence, so a language switch re-words a
 * refusal already on screen.
 */
type FormProblem =
  /** The form stopped before asking: no vehicle type is chosen. */
  | { readonly kind: 'noType' }
  /** A refused request, with what it said. */
  | { readonly kind: 'request'; readonly snapshot: ProblemSnapshot };

/** Words a problem in the reader's language, at render time. The code mappings are this form's own. */
function describe(
  problem: FormProblem,
  t: (key: TranslationKey) => string,
  language: Language,
): string {
  if (problem.kind === 'noType') return t('carForm.chooseAVehicleType');
  const p = problem.snapshot;
  if (p.code === 'dealer.not_approved') return t('carForm.yourDealershipIsNot');
  if (p.code === 'vehicle.plate_taken') return t('carForm.aCarWithThat');
  return serverSentence(p, language, t) ?? t('carForm.theServiceDidNot');
}

/** An API name as the reader's language says it; a name this build has no word for is shown as sent. */
function wordFor(
  labels: Readonly<Record<string, TranslationKey>>,
  name: string,
  t: (key: TranslationKey) => string,
): string {
  const key = labels[name];
  return key ? t(key) : name;
}
