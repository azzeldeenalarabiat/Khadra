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
import { LookupEntry, LookupsService } from '../../core/services/lookups.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { MapComponent } from '../../shared/map/map.component';
import { ImageFallbackDirective } from '../../shared/image-fallback.directive';
import { IconName } from '../../shared/icon/icon-paths';
import { I18nService } from '../../core/i18n/i18n.service';
import { FormatService } from '../../core/i18n/format.service';
import { TranslationKey } from '../../core/i18n/en';
import { Language } from '../../core/i18n/language';
import {
  ProblemSnapshot,
  fieldMessage,
  fieldMessageFor,
  serverSentence,
  snapshotProblem,
} from '../../core/i18n/problem';
import { CONTENT_LANGUAGES, boxErrorNames, boxKey } from '../../core/i18n/bilingual-content';
import { MoneyPipe } from '../../shared/money.pipe';

interface Step {
  readonly n: number;
  /** A key, worded where the step is shown, so the list follows a language switch. */
  readonly title: TranslationKey;
  readonly icon: IconName;
}

const STEPS: readonly Step[] = [
  { n: 1, title: 'vehicleWizard.basicInformation', icon: 'info' },
  { n: 2, title: 'vehicleWizard.specifications', icon: 'gear' },
  { n: 3, title: 'vehicleWizard.pricing', icon: 'currency-circle-dollar' },
  { n: 4, title: 'dealerProfile.location', icon: 'map-pin' },
  { n: 5, title: 'carForm.photos', icon: 'image' },
  { n: 6, title: 'vehicleWizard.availability', icon: 'toggle-right' },
  { n: 7, title: 'common.delivery', icon: 'moped' },
  { n: 8, title: 'vehicleWizard.review', icon: 'check-square' },
];

/**
 * The API's transmission names, each with the words a reader sees. The name is what the form sends
 * and the server stores; only the label is translated, in the platform's own vocabulary (the one
 * `/api/v1/app-config` gives the customer app for the same car).
 */
const TRANSMISSION_LABELS: Readonly<Record<string, TranslationKey>> = {
  Automatic: 'fleetList.transmissionAutomatic',
  Manual: 'fleetList.transmissionManual',
};

/** The API's fuel type names, arranged the same way. */
const FUEL_TYPE_LABELS: Readonly<Record<string, TranslationKey>> = {
  Petrol: 'fleetList.fuelPetrol',
  Diesel: 'fleetList.fuelDiesel',
  Hybrid: 'fleetList.fuelHybrid',
  Electric: 'fleetList.fuelElectric',
};

/**
 * The wizard's working copy of a vehicle.
 *
 * Identical to `VehicleRequest` except that the four facts a dealer must state about THIS car — its
 * year, its seats, its price and its deposit — are nullable here, so "not answered yet" is a state
 * the form can hold and refuse to advance past. `VehicleRequest` keeps them required, because by
 * the time anything is sent they have all been answered.
 */
type WizardForm = Omit<VehicleRequest, 'year' | 'seats' | 'dailyRate' | 'securityDeposit'> & {
  readonly year: number | null;
  readonly seats: number | null;
  readonly dailyRate: number | null;
  readonly securityDeposit: number | null;
};

/**
 * Every step has validated by the time this runs, so the four are answered; the guard is here
 * because a future step reorder must fail loudly rather than post a null price to the API.
 */
function toRequest(form: WizardForm): VehicleRequest {
  if (
    form.year === null ||
    form.seats === null ||
    form.dailyRate === null ||
    form.securityDeposit === null
  ) {
    throw new Error('The vehicle form was submitted before every required answer was given.');
  }
  return {
    ...form,
    year: form.year,
    seats: form.seats,
    dailyRate: form.dailyRate,
    securityDeposit: form.securityDeposit,
  };
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
 * dealership's own and the same for every car it delivers, so it is shown here as a fact and changed
 * on the Delivery page.
 */
@Component({
  selector: 'kh-vehicle-wizard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './vehicle-wizard.component.html',
  imports: [RouterLink, IconComponent, ImageFallbackDirective, MapComponent, MoneyPipe],
})
export class VehicleWizardComponent {
  private readonly i18n = inject(I18nService);
  protected readonly t = this.i18n.t;
  protected readonly formats = inject(FormatService);
  private readonly service = inject(FleetService);
  private readonly consoleData = inject(DealerConsoleService);
  private readonly ui = inject(ConsoleUiService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly steps = STEPS;

  protected readonly step = signal(1);
  protected readonly busy = signal(false);
  protected readonly uploading = signal(false);
  /** What the banner reports, held as facts; `problemText` chooses the words. */
  private readonly problem = signal<WizardProblem | null>(null);
  /** The refused save whose per-field messages sit under the fields, until the next save. */
  private readonly fieldProblem = signal<ProblemSnapshot | null>(null);
  protected readonly draft = signal<Vehicle | null>(null);
  protected readonly publishOnSave = signal(true);
  /** A draft of the dealer's own that owns the plate they just typed; offered as "continue that". */
  protected readonly resumable = signal<Vehicle | null>(null);

  protected readonly problemText = computed(() => {
    const problem = this.problem();
    return problem ? describe(problem, this.t, this.i18n.lang()) : null;
  });

  constructor() {
    // `?draft=<id>` means this wizard is continuing a draft created earlier (a refresh, or a return
    // from the fleet). The form is rebuilt from the car itself, never from anything cached here.
    const draftId = this.route.snapshot.queryParamMap.get('draft');
    if (draftId) this.service.editing.set(draftId);
    effect(() => {
      const existing = this.draftCar();
      if (draftId && existing && existing.vehicleId === draftId && !this.draft())
        this.hydrate(existing);
    });
    // The `<select>` displays its first option regardless of the model, so an unset id would look
    // chosen and step 1 would pass with nothing selected. Fills a blank only.
    effect(() => {
      const first = this.carTypes()?.[0];
      if (first && !this.form().carTypeId) this.patch({ carTypeId: first.id });
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
  /** The API's names, in the order offered. Shown through `transmissionLabel` / `fuelTypeLabel`. */
  protected readonly transmissions = Object.keys(TRANSMISSION_LABELS);
  protected readonly fuelTypes = Object.keys(FUEL_TYPE_LABELS);
  protected readonly seatOptions = [2, 4, 5, 7, 8];

  /**
   * Nothing about the car is pre-filled, and that is the point.
   *
   * This form used to open on make "Toyota", 30 JOD a day and a 150 JOD deposit — a make, a price
   * and a deposit chosen by nobody. A dealer who tabbed past them published a real car at figures
   * the console invented, and there was no way afterwards to tell an invented 30 from a deliberate
   * one. Every field below starts empty and the step refuses to advance until the dealer answers.
   *
   * The numbers are nullable rather than zero so "not answered yet" and "answered zero" stay
   * different questions: a dealer may legitimately ask for no deposit, and a 0 sitting in the box
   * from the start would publish that choice on their behalf.
   */
  protected readonly form = signal<WizardForm>({
    carTypeId: '',
    make: '',
    model: '',
    year: null,
    color: null,
    seats: null,
    transmission: '',
    fuelType: '',
    description: { ar: null, en: null },
    plateNumber: '',
    dailyRate: null,
    securityDeposit: null,
    isDeliveryEligible: false,
    mileageUnlimited: true,
    mileageDailyLimitKm: null,
    mileageExcessFeePerKm: null,
    fuelPolicy: 'FullToFull',
  });

  protected readonly me = this.consoleData.me;
  protected readonly delivery = this.consoleData.delivery;
  protected readonly dealer = loaded(this.me);
  protected readonly deliverySettings = loaded(this.delivery);
  private readonly draftCar = loaded(this.service.vehicle);
  private readonly ownFleet = loaded(this.service.vehicles);
  /** The platform's vehicle categories. The id was a literal here too, chosen by nobody. */
  private readonly lookups = inject(LookupsService);
  protected readonly carTypes = loaded(this.lookups.carTypes);
  protected readonly carTypesFailure = computed(() =>
    this.lookups.carTypes.error() ? this.t('vehicleWizard.vehicleTypesCouldNot') : null,
  );

  /**
   * Newest first, from the platform's own bounds.
   *
   * This was `Array.from({ length: 12 }, …)` — twelve years ending at the current one — so the
   * oldest car anyone could list was 2016, and the domain would have refused anything before 1990
   * regardless. Empty until the range arrives rather than guessing one.
   */
  private readonly yearRange = loaded(this.lookups.modelYears);
  protected readonly years = computed(() => {
    const range = this.yearRange();
    if (!range) return [];
    const count = range.latest - range.earliest + 1;
    return count > 0 ? Array.from({ length: count }, (_, i) => range.latest - i) : [];
  });
  /** Against the server's bounds, so the form refuses exactly what the API would refuse. */
  private yearAllowed(year: number | null): boolean {
    const range = this.yearRange();
    return year !== null && !!range && year >= range.earliest && year <= range.latest;
  }

  protected readonly current = computed(() => STEPS[this.step() - 1]);
  protected readonly photos = computed(() => this.draft()?.images ?? []);

  /** "Step 3 of 8" as one message, so each language places the two numbers its own way. */
  protected readonly stepOfTotal = computed(() =>
    this.t('vehicleWizard.stepOfTotal', { current: this.step(), total: STEPS.length }),
  );

  /**
   * The pickup note around its link to the dealer profile.
   *
   * The dictionary writes `{profile}` where each language's grammar puts the link, and the sentence
   * is cut there, so it is never glued together from fragments in English order. The placeholder is
   * left unfilled on purpose: `t` returns a placeholder it was given no value for as written.
   */
  protected readonly pickupNote = computed(() => {
    const [before, after = ''] = this.t('vehicleWizard.pickupFromYourLocation').split('{profile}');
    return { before, after };
  });

  protected readonly stepValid = computed(() => {
    const f = this.form();
    switch (this.step()) {
      case 1:
        return (
          f.carTypeId.length > 0 &&
          f.make.trim().length > 0 &&
          f.model.trim().length > 0 &&
          this.yearAllowed(f.year)
        );
      case 2:
        return (
          f.plateNumber.trim().length > 0 &&
          (f.seats ?? 0) > 0 &&
          f.transmission.length > 0 &&
          f.fuelType.length > 0
        );
      case 3:
        return (
          (f.dailyRate ?? 0) > 0 &&
          f.securityDeposit !== null &&
          f.securityDeposit >= 0 &&
          (f.mileageUnlimited ||
            ((f.mileageDailyLimitKm ?? 0) > 0 && (f.mileageExcessFeePerKm ?? 0) >= 0))
        );
      default:
        return true;
    }
  });

  /**
   * The review step's money, in the currency the platform holds THIS car's figures in.
   *
   * The amounts are the form's, which is what saving sends. The codes are the draft's own: the draft
   * exists by the review step (reaching Photos creates it, and every price edit passes Photos again
   * on the way back), so no code is assumed.
   */
  protected readonly reviewPrice = computed(() =>
    this.formats.money(this.form().dailyRate, this.draft()?.dailyRate.currency),
  );

  protected readonly reviewPriceNote = computed(() =>
    this.t('vehicleWizard.perDayAndDeposit', {
      deposit: this.formats.money(
        this.form().securityDeposit,
        this.draft()?.securityDeposit.currency,
      ),
    }),
  );

  /** "Automatic · Petrol · 5 seats" under the car's name on the review step. */
  protected readonly reviewSpecs = computed(() => {
    const f = this.form();
    const facts = [this.transmissionLabel(f.transmission), this.fuelTypeLabel(f.fuelType)];
    if (f.seats !== null) facts.push(this.t('fleetList.seatCount', { count: f.seats }));
    return facts.join(' · ');
  });

  protected readonly review = computed<readonly KeyValue[]>(() => {
    const f = this.form();
    const me = this.dealer();
    const fee = this.deliverySettings()?.fee;
    return [
      { k: this.t('dealerBooking.plate'), v: f.plateNumber || '—' },
      { k: this.t('common.colour'), v: f.color || '—' },
      {
        k: this.t('dealerProfile.location'),
        v: me ? `${me.businessName} · ${this.formats.coordinates(me.latitude, me.longitude)}` : '—',
      },
      {
        k: this.t('vehicleWizard.listing'),
        v: this.publishOnSave()
          ? this.t('vehicleWizard.publishedOnSave')
          : this.t('vehicleWizard.keptAsADraft'),
      },
      {
        k: this.t('vehicleWizard.mileage'),
        v: f.mileageUnlimited
          ? this.t('vehicleWizard.unlimited')
          : this.t('dealerBooking.mileageAllowance', {
              limit: this.formats.number(f.mileageDailyLimitKm),
              fee: this.formats.money(
                f.mileageExcessFeePerKm,
                this.draft()?.mileage.excessFeePerKm?.currency,
              ),
            }),
      },
      {
        k: this.t('common.fuelPolicy'),
        v:
          f.fuelPolicy === 'FullToFull'
            ? this.t('vehicleWizard.fullToFull')
            : this.t('vehicleWizard.sameToSame'),
      },
      {
        k: this.t('common.delivery'),
        v: f.isDeliveryEligible
          ? fee
            ? this.t('vehicleWizard.eligibleYourFee', {
                fee: this.formats.money(fee.amount, fee.currency),
              })
            : this.t('vehicleWizard.eligible')
          : this.t('vehicleDetail.pickupOnly'),
      },
      {
        k: this.t('carForm.photos'),
        v: this.t('vehicleWizard.photosUploaded', { count: this.photos().length }),
      },
      {
        k: this.t('vehicleDetail.insurance'),
        v: this.t('vehicleDetail.pendingPlatformConfiguration'),
        tone: 'dim',
      },
    ];
  });

  protected readonly footer = computed(() => {
    if (this.step() === 8) return this.t('vehicleWizard.nothingIsPublishedUntil');
    return this.draft()
      ? this.t('vehicleWizard.savedAsADraft')
      : this.t('vehicleWizard.becomesADraftOnce');
  });

  protected patch(patch: Partial<WizardForm>): void {
    this.form.update((f) => ({ ...f, ...patch }));
  }

  protected text(event: Event): string {
    return (event.target as HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement).value;
  }

  /** An empty box is null, not 0: see the form above for why the difference matters. */
  protected number(event: Event): number | null {
    const raw = (event.target as HTMLInputElement).value;
    return raw.trim() === '' ? null : Number(raw);
  }

  protected fieldError(name: string): string | null {
    return fieldMessage(this.fieldProblem(), name, this.i18n.lang(), this.t);
  }

  /** The two boxes the description is written in, in the order the wizard draws them. */
  protected readonly languages = CONTENT_LANGUAGES;

  protected descriptionText(language: Language): string {
    return this.form().description[language] ?? '';
  }

  /** Empty is null — nothing written — which is what lets the other language stand in for it. */
  protected setDescription(language: Language, event: Event): void {
    const written = this.text(event);
    this.patch({
      description: {
        ...this.form().description,
        [language]: written.trim() === '' ? null : written,
      },
    });
  }

  protected descriptionId(language: Language): string {
    return 'w-' + boxKey('description', language);
  }

  protected descriptionLabel(language: Language): string {
    return this.i18n.languageName(language);
  }

  /** What the server said about ONE box, under every name that box can arrive under. */
  protected descriptionError(language: Language): string | null {
    return fieldMessageFor(
      this.fieldProblem(),
      boxErrorNames('description', language),
      this.i18n.lang(),
      this.t,
    );
  }

  /** A transmission as the reader's language says it. A name this build has no word for is shown as sent. */
  protected transmissionLabel(name: string): string {
    return wordFor(TRANSMISSION_LABELS, name, this.t);
  }

  /** A fuel type as the reader's language says it, the same way. */
  protected fuelTypeLabel(name: string): string {
    return wordFor(FUEL_TYPE_LABELS, name, this.t);
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
    this.fieldProblem.set(null);
    try {
      const draft = this.draft();
      const saved = draft
        ? await this.service.update(draft.vehicleId, toRequest(this.form()))
        : await this.service.add(toRequest(this.form()));
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
      const snapshot = snapshotProblem(error);
      const plateTaken = snapshot.code === 'vehicle.plate_taken';
      this.fieldProblem.set(snapshot);
      this.problem.set(plateTaken ? this.plateTaken() : { kind: 'save', snapshot });
      // Field errors belong to the early steps; go back to the first one that can show them.
      if (snapshot.errors) this.step.set(1);
      if (plateTaken) this.step.set(2);
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
      this.problem.set({ kind: 'upload', snapshot: snapshotProblem(error) });
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
        this.problem.set({ kind: 'noPhoto' });
        this.step.set(5);
        return;
      }
      this.busy.set(true);
      try {
        await this.service.changeStatus(draft.vehicleId, 'Publish');
        this.service.refresh();
      } catch (error) {
        this.problem.set({ kind: 'publish', snapshot: snapshotProblem(error) });
        this.busy.set(false);
        return;
      } finally {
        this.busy.set(false);
      }
    }
    const f = this.form();
    const vehicle = `${f.make} ${f.model} ${f.year}`;
    this.ui.showToast(
      this.publishOnSave()
        ? this.t('vehicleWizard.vehiclePublished')
        : this.t('vehicleWizard.draftSaved'),
      this.publishOnSave()
        ? this.t('vehicleWizard.vehicleIsLive', { vehicle })
        : this.t('vehicleWizard.vehicleIsADraft', { vehicle }),
    );
    await this.router.navigate(['/dealer/fleet', draft.vehicleId]);
  }

  protected async cancel(): Promise<void> {
    const draft = this.draft();
    if (draft) {
      // Leaving keeps the draft, so the draft must hold what the form holds.
      await this.persist();
      this.ui.showToast(
        this.t('vehicleWizard.draftKept'),
        this.t('vehicleWizard.vehicleStaysADraft', { vehicle: `${draft.make} ${draft.model}` }),
        'warn',
      );
    }
    await this.router.navigate(['/dealer/fleet']);
  }

  /** A "taken" plate is very often the dealer's own abandoned draft; say so and point at it. */
  private plateTaken(): WizardProblem {
    const plate = this.form().plateNumber.trim();
    const own = (this.ownFleet() ?? []).find((car) => car.plateNumber === plate) ?? null;
    if (own?.status === 'Draft') this.resumable.set(own);
    return { kind: 'plateTaken', plate, own };
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
    this.fieldProblem.set(null);
    this.step.set(5);
  }
}

/**
 * What the banner reports, held as facts and worded by `describe` when it is shown, so a language
 * switch re-words a message already on screen.
 */
type WizardProblem =
  /** Publishing was chosen with no photo: the wizard stops before asking the server. */
  | { readonly kind: 'noPhoto' }
  /** A refused request, and which one: each says something different when the server gives no reason. */
  | { readonly kind: 'save' | 'upload' | 'publish'; readonly snapshot: ProblemSnapshot }
  /** The plate as it was sent, and the dealer's own car already carrying it when there is one. */
  | { readonly kind: 'plateTaken'; readonly plate: string; readonly own: Vehicle | null };

/** Words a problem in the reader's language, at render time. The code mappings are the screen's own. */
function describe(problem: WizardProblem, t: I18nService['t'], language: Language): string {
  if (problem.kind === 'noPhoto') return t('vehicleWizard.addAtLeastOne');
  if (problem.kind === 'plateTaken') {
    const { plate, own } = problem;
    if (!own) return t('vehicleWizard.thatPlateIsAlready');
    const vehicle = `${own.make} ${own.model} ${own.year}`;
    return own.status === 'Draft'
      ? t('vehicleWizard.plateIsOnYourDraft', { plate, vehicle })
      : t('vehicleWizard.plateIsOnYourCar', { plate, vehicle });
  }
  const p = problem.snapshot;
  if (problem.kind === 'upload') {
    return p.code === 'vehicle.invalid_image_type'
      ? t('vehicleWizard.useAJpegPng')
      : (serverSentence(p, language, t) ?? t('vehicleWizard.theUploadDidNot'));
  }
  if (problem.kind === 'publish') {
    if (p.code === 'dealer.not_approved') return t('vehicleWizard.yourDealershipCannotTrade');
    // The draft WAS saved a moment earlier, so the general refusal line ("nothing has been changed")
    // would be untrue here. English keeps the server's own sentence; Arabic says what happened.
    return (
      (language === 'en' ? serverSentence(p, language, t) : null) ??
      t('vehicleWizard.theCarWasSaved')
    );
  }
  return serverSentence(p, language, t) ?? t('dealerDelivery.serviceDidNotRespond');
}

/** An API name as the reader's language says it; a name this build has no word for is shown as sent. */
function wordFor(
  labels: Readonly<Record<string, TranslationKey>>,
  name: string,
  t: I18nService['t'],
): string {
  const key = labels[name];
  return key ? t(key) : name;
}
