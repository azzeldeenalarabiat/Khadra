import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { DealerConsoleService } from '../../core/services/dealer-console.service';
import { LookupsService } from '../../core/services/lookups.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { MapComponent } from '../../shared/map/map.component';
import { TranslationKey } from '../../core/i18n/en';
import { I18nService } from '../../core/i18n/i18n.service';

/** The three papers spec 3.1 requires. The keys are the form field names the API binds. */
const REQUIRED_DOCUMENTS = [
  {
    key: 'commercialRegistration',
    labelKey: 'dealerProfile.commercialRegistration',
    hintKey: 'dealerApply.theRegistrationCertificateFor',
  },
  {
    key: 'vehicleRegistration',
    labelKey: 'dealerApply.greenPlateVehicleRegistration',
    hintKey: 'dealerApply.proofThatYourCars',
  },
  {
    key: 'ownerIdentity',
    labelKey: 'dealerApply.theOwnersId',
    hintKey: 'dealerApply.yourNationalIdOr',
  },
] as const satisfies readonly { key: string; labelKey: TranslationKey; hintKey: TranslationKey }[];

type DocumentKey = (typeof REQUIRED_DOCUMENTS)[number]['key'];

/**
 * Step two of spec 3.1: the gallery itself is filed for the platform's licence check.
 *
 * This is the screen that was missing. An owner could register (step one) and then had nowhere to
 * go: `GET /dealers/me` answered 404, the console gate read that as a failure, and the endpoint that
 * creates the dealership — `POST /api/v1/dealers`, which has existed all along — had no caller.
 *
 * Everything the applicant sees comes from somewhere real. The cities are the platform's active
 * list; the map is centred on the chosen city's own recorded centre. Nothing here invents a default
 * location: a coordinate the applicant never chose is a coordinate a customer would be sent to.
 *
 * The dealership is created PENDING_REVIEW, which starts the platform's review clock, so the button
 * says "submit" rather than reading like a draft that can be edited later.
 */
@Component({
  selector: 'kh-dealer-apply',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dealer-apply.component.html',
  imports: [IconComponent, MapComponent],
})
export class DealerApplyComponent {
  private readonly i18n = inject(I18nService);
  protected readonly t = this.i18n.t;

  /**
   * The city list in the reader's own language.
   *
   * The dropdown printed `nameEn` whatever the language was, so an Arabic applicant chose their
   * city from a list of English names in an otherwise Arabic, right-to-left form. Both names come
   * from the same curated row, so this is a display choice and not a second source of truth; the
   * value submitted is the row's id either way. Falls back to the other name rather than showing an
   * empty option, because a lookup row is allowed to be half-translated and an unnamed option is
   * unpickable.
   */
  protected readonly cityOptions = computed(() => {
    const arabic = this.i18n.lang() === 'ar';
    return this.cities().map((city) => ({
      id: city.id,
      name: (arabic ? city.nameAr || city.nameEn : city.nameEn || city.nameAr).trim(),
    }));
  });
  private readonly console = inject(DealerConsoleService);
  private readonly lookups = inject(LookupsService);
  private readonly ui = inject(ConsoleUiService);
  private readonly router = inject(Router);

  protected readonly documents = REQUIRED_DOCUMENTS;

  protected readonly businessName = signal('');
  protected readonly registrationNumber = signal('');
  protected readonly description = signal('');
  protected readonly cityId = signal('');
  protected readonly latitude = signal('');
  protected readonly longitude = signal('');
  protected readonly opensAt = signal('');
  protected readonly closesAt = signal('');
  protected readonly files = signal<Readonly<Partial<Record<DocumentKey, File>>>>({});

  protected readonly busy = signal(false);
  protected readonly problem = signal<string | null>(null);

  protected readonly cities = computed(() => loaded(this.lookups.cities)() ?? []);

  /** The chosen city's own recorded centre, when it has one. The only source of a starting pin. */
  private readonly cityCentre = computed(() => {
    const city = this.cities().find((candidate) => candidate.id === this.cityId());
    return city?.centreLatitude != null && city.centreLongitude != null
      ? { latitude: city.centreLatitude, longitude: city.centreLongitude }
      : null;
  });

  /**
   * Whether the city the applicant picked has been pinned by an administrator.
   *
   * Said out loud rather than silently falling back to somewhere in Amman, because that fallback
   * would attach a location nobody chose to a real business.
   */
  protected readonly cityHasNoCentre = computed(() => !!this.cityId() && !this.cityCentre());

  protected readonly coordsValid = computed(() => {
    if (this.latitude() === '' || this.longitude() === '') return false;
    const lat = Number(this.latitude());
    const lng = Number(this.longitude());
    return lat >= -90 && lat <= 90 && lng >= -180 && lng <= 180;
  });

  protected readonly latNumber = computed(() => Number(this.latitude()));
  protected readonly lngNumber = computed(() => Number(this.longitude()));

  protected readonly area = signal('');
  protected readonly street = signal('');
  /** Whether the applicant has typed here. A suggestion never overwrites their own words. */
  private readonly areaTouched = signal(false);
  private readonly streetTouched = signal(false);
  protected readonly suggesting = signal(false);
  /**
   * The provider's own licence line, printed beside the fields it filled.
   *
   * From the server rather than written into the template: open-data licences require attribution
   * where the data is shown, and a literal here would be a claim about the source that stops being
   * true the day the provider changes.
   */
  protected readonly attribution = signal<string | null>(null);

  protected readonly locating = signal(false);
  /** An i18n KEY, never a sentence: a stored sentence stays in the language it was written in. */
  protected readonly locationProblem = signal<TranslationKey | null>(null);

  /**
   * Where the map LOOKS, which is not the same question as where the gallery is.
   *
   * Until a pin is placed there is no answer to the second question, and inventing one would be the
   * worst outcome: a pin at a default centre reads as a location the applicant chose, and they could
   * submit it without ever noticing. So the camera opens on the country, the map draws no pin (see
   * kh-map's `hasPin`), and the first click is what makes a choice.
   *
   * These constants are a viewport, not business data. Nothing is stored from them, nothing is shown
   * as a fact, and the moment a pin exists they stop being consulted. The city's own recorded centre
   * takes over as soon as an administrator has pinned one and the applicant picks it.
   */
  private static readonly CountryView = { latitude: 31.24, longitude: 36.51, zoom: 7 } as const;

  protected readonly mapLatitude = computed(() =>
    this.coordsValid() ? this.latNumber() : DealerApplyComponent.CountryView.latitude,
  );
  protected readonly mapLongitude = computed(() =>
    this.coordsValid() ? this.lngNumber() : DealerApplyComponent.CountryView.longitude,
  );
  protected readonly mapZoom = computed(() =>
    this.coordsValid() ? 14 : DealerApplyComponent.CountryView.zoom,
  );

  protected readonly missingDocuments = computed(() =>
    REQUIRED_DOCUMENTS.filter((document) => !this.files()[document.key]).map((document) =>
      this.t(document.labelKey),
    ),
  );

  protected readonly canSubmit = computed(
    () =>
      !this.busy() &&
      this.businessName().trim().length > 1 &&
      this.registrationNumber().trim().length > 0 &&
      this.coordsValid() &&
      !!this.opensAt() &&
      !!this.closesAt() &&
      this.missingDocuments().length === 0,
  );

  protected fieldValue(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement).value;
  }

  /** Picking a city moves the map to that city's centre, unless a pin has already been placed. */
  protected chooseCity(id: string): void {
    const placed = this.coordsValid();
    this.cityId.set(id);
    const centre = this.cityCentre();
    if (centre && !placed) {
      this.latitude.set(String(centre.latitude));
      this.longitude.set(String(centre.longitude));
    }
  }

  protected moveTo(point: { latitude: number; longitude: number }): void {
    this.locationProblem.set(null);
    this.latitude.set(String(point.latitude));
    this.longitude.set(String(point.longitude));
    void this.suggestFor(point.latitude, point.longitude);
  }

  /**
   * Offers what the pin might be called, into fields the applicant can still change.
   *
   * Only from a DELIBERATE pin move — a click or a drag — never from picking a city, which sets the
   * coordinates to that city's centre: geocoding a city centre returns some arbitrary downtown
   * street and would fill the form with an address belonging to nobody.
   *
   * Nothing the applicant has typed is overwritten. A suggestion is a suggestion; silently replacing
   * a corrected area on the next nudge of the pin is how somebody submits an address they had
   * already fixed.
   */
  private async suggestFor(latitude: number, longitude: number): Promise<void> {
    this.suggesting.set(true);
    try {
      const suggestion = await this.console.suggestAddress(latitude, longitude, this.i18n.lang());
      // Null covers every ordinary failure -- switched off, rate limited, nothing recorded there.
      // The applicant types the address, which is what they did before this existed.
      if (!suggestion) return;

      if (!this.areaTouched() && suggestion.area) this.area.set(suggestion.area);
      if (!this.streetTouched() && suggestion.street) this.street.set(suggestion.street);
      // Only ever a pre-selection, and only when the server matched exactly one curated city.
      if (!this.cityId() && suggestion.suggestedCityId) this.cityId.set(suggestion.suggestedCityId);
      this.attribution.set(suggestion.attribution);
    } finally {
      this.suggesting.set(false);
    }
  }

  /** Typing marks the field as the applicant's, so no later suggestion overwrites it. */
  protected typeArea(value: string): void {
    this.areaTouched.set(true);
    this.area.set(value);
  }

  protected typeStreet(value: string): void {
    this.streetTouched.set(true);
    this.street.set(value);
  }

  /** Takes the pin off the map and empties the two fields, so nothing half-chosen is submitted. */
  protected clearPin(): void {
    this.locationProblem.set(null);
    this.latitude.set('');
    this.longitude.set('');
  }

  /**
   * Drops the pin where the browser says the applicant is.
   *
   * Every branch reports a REASON rather than failing quietly. A permission prompt the applicant
   * dismissed, a device with no fix, and a browser too old to have the API are three different
   * situations with three different remedies, and "nothing happened" covers all of them badly.
   * The map stays usable by hand whichever one it is.
   */
  protected useMyLocation(): void {
    if (!navigator.geolocation) {
      this.locationProblem.set('dealerApply.thisBrowserCannot');
      return;
    }

    this.locating.set(true);
    this.locationProblem.set(null);
    navigator.geolocation.getCurrentPosition(
      (position) => {
        this.locating.set(false);
        this.moveTo({
          latitude: position.coords.latitude,
          longitude: position.coords.longitude,
        });
      },
      (error) => {
        this.locating.set(false);
        this.locationProblem.set(
          error.code === error.PERMISSION_DENIED
            ? 'dealerApply.locationPermissionRefused'
            : 'dealerApply.couldNotFindYou',
        );
      },
      // A gallery is a fixed address, so a slow accurate fix beats a fast vague one -- and a cached
      // position from another part of town would put the pin somewhere plausible and wrong.
      { enableHighAccuracy: true, timeout: 15_000, maximumAge: 0 },
    );
  }

  protected attach(key: DocumentKey, event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    this.files.update((current) => ({ ...current, [key]: file }));
  }

  protected fileName(key: DocumentKey): string | null {
    return this.files()[key]?.name ?? null;
  }

  protected async submit(): Promise<void> {
    if (!this.canSubmit()) return;

    this.busy.set(true);
    this.problem.set(null);

    const form = new FormData();
    form.append('businessName', this.businessName().trim());
    form.append('commercialRegistrationNumber', this.registrationNumber().trim());
    form.append('latitude', this.latitude());
    form.append('longitude', this.longitude());
    form.append('opensAt', this.opensAt());
    form.append('closesAt', this.closesAt());
    if (this.description().trim()) form.append('description', this.description().trim());
    if (this.cityId()) form.append('cityId', this.cityId());
    // What is in the fields, which is what the applicant confirmed -- not what the geocoder said.
    if (this.area().trim()) form.append('addressArea', this.area().trim());
    if (this.street().trim()) form.append('addressStreet', this.street().trim());
    for (const document of REQUIRED_DOCUMENTS) {
      const file = this.files()[document.key];
      if (file) form.append(document.key, file, file.name);
    }

    try {
      const dealer = await this.console.submitApplication(form);
      this.ui.showToast(
        this.t('dealerApply.applicationSubmitted'),
        `${dealer.businessName} is with the platform for its licence check.`,
      );
      await this.router.navigateByUrl('/dealer/dashboard');
    } catch (error) {
      this.problem.set(describe(error, this.t));
    } finally {
      this.busy.set(false);
    }
  }
}

function describe(error: unknown, t: (key: TranslationKey) => string): string {
  if (!(error instanceof HttpErrorResponse)) {
    return t('dealerApply.theServiceDidNot');
  }

  const code: string | undefined = error.error?.code;
  switch (code) {
    case 'dealer.already_registered':
    // The database's own answer to the same question, from the unique index on the owner. The
    // handler's check and the index can only disagree in a race — two tabs, or a double click on a
    // slow multipart — and the person on the other end needs the same sentence either way.
    case 'data.conflict':
      return t('dealerApply.thisAccountHasAlready');
    case 'dealer.commercial_registration_taken':
      return t('dealerApply.aGalleryIsAlready');
    case 'dealer.missing_required_documents':
      return t('dealerApply.allThreeDocumentsAre');
    case 'dealer.document_too_large':
    case 'documents.too_large':
      return t('dealerApply.oneOfTheFiles');
    case 'dealer.invalid_document_content':
    case 'documents.invalid_content':
      return t('dealerApply.uploadEachDocumentAs');
    case 'dealer.invalid_operating_hours':
      return t('dealerApply.closingTimeMustBe');
    default:
      break;
  }

  if (error.status === 413) {
    return t('dealerApply.theDocumentsTogetherAre');
  }
  return error.error?.title ?? t('dealerApply.theApplicationWasRejected');
}
