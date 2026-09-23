import { isPlatformBrowser } from '@angular/common';
import { ChangeDetectionStrategy, Component, PLATFORM_ID, computed, effect, inject, input } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { vocabularyLabel } from '../../core/api/app-config.api';
import { CatalogueVehicle, PublicGalleryPage, RentalQuote } from '../../core/api/catalogue.api';
import { LookupsService } from '../../core/api/lookups.service';
import { ShortlistService } from '../../core/api/shortlist.service';
import { AppConfigService } from '../../core/config/app-config.service';
import { snapshotProblem } from '../../core/http/problem';
import { injectResponseStatus } from '../../core/http/server-context';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { wallClockToInstant } from '../../core/i18n/zoned-time';
import { idFromSlug, slugFor } from '../../core/routing/slug';
import { SeoService } from '../../core/seo/seo.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { OfficeTextComponent } from '../../shared/office-text/office-text.component';
import { OpeningHoursComponent } from '../../shared/office-text/opening-hours.component';
import { PhotoGalleryComponent } from '../../shared/photo-gallery/photo-gallery.component';
import { SaveButtonComponent } from '../../shared/save-button/save-button.component';
import { SearchFormComponent, SearchFormValue } from '../../shared/search-form/search-form.component';
import { StatePanelComponent } from '../../shared/state/state-panel.component';
import { EMPTY_SEARCH, parseSearch, searchToParams } from '../cars/car-search';
import { httpData } from '../../core/http/http-data';

/**
 * One car. The page is the car, its office, and what the office wrote about renting from it; the price
 * card is the server's quote for the dates the customer chose, never a sum worked out here.
 *
 * The car itself is fetched WITHOUT dates, so the server-rendered page is the same for everyone and
 * safe to index. Availability and price come from the quote, which the browser asks for once dates
 * are set.
 */
@Component({
  selector: 'kh-car-details',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    IconComponent,
    PhotoGalleryComponent,
    SaveButtonComponent,
    OfficeTextComponent,
    OpeningHoursComponent,
    StatePanelComponent,
    SearchFormComponent,
  ],
  templateUrl: './car-details.component.html',
})
export class CarDetailsComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly format = inject(FormatService);
  private readonly lookups = inject(LookupsService);
  private readonly appConfig = inject(AppConfigService);
  private readonly router = inject(Router);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  readonly slug = input<string>('');
  protected readonly vehicleId = computed(() => idFromSlug(this.slug()));

  private readonly params = toSignal(inject(ActivatedRoute).queryParams, { initialValue: {} });
  protected readonly period = computed(() => {
    const search = parseSearch(this.params());
    return search.from && search.to ? { from: search.from, to: search.to } : null;
  });

  protected readonly car = httpData<CatalogueVehicle>(() => {
    const id = this.vehicleId();
    return id ? `/api/v1/vehicles/${id}` : undefined;
  });

  protected readonly office = httpData<PublicGalleryPage>(() => {
    const car = this.car.value();
    return car ? `/api/v1/galleries/${car.gallery.dealerId}` : undefined;
  });

  protected readonly quote = httpData<RentalQuote>(() => {
    const id = this.vehicleId();
    const period = this.period();
    const zone = this.format.timeZone();
    if (!this.isBrowser || !id || !period || !zone) return undefined;
    const pickup = wallClockToInstant(period.from, zone);
    const dropoff = wallClockToInstant(period.to, zone);
    if (!pickup || !dropoff) return undefined;
    return {
      url: `/api/v1/vehicles/${id}/quote`,
      params: { pickupAt: pickup.toISOString(), returnAt: dropoff.toISOString(), pickupMethod: 'SelfPickup' },
    };
  });

  protected readonly problem = computed(() => (this.car.error() ? snapshotProblem(this.car.error()) : null));
  protected readonly notFound = computed(() => !this.vehicleId() || this.problem()?.status === 404);
  protected readonly quoteProblem = computed(() => (this.quote.error() ? snapshotProblem(this.quote.error()) : null));

  protected readonly name = computed(() => {
    const car = this.car.value();
    return car ? `${car.make} ${car.model} ${car.year}` : '';
  });
  protected readonly city = computed(() => this.lookups.cityName(this.car.value()?.gallery.cityId));
  protected readonly officeLink = computed(() => {
    const car = this.car.value();
    return car ? this.i18n.link('dealers', slugFor(car.gallery.dealerId, car.gallery.businessName)) : [];
  });
  protected readonly transmission = computed(() =>
    vocabularyLabel(this.appConfig.config()?.vocabularies.transmissions, this.car.value()?.transmission, this.i18n.isArabic()),
  );
  protected readonly fuel = computed(() =>
    vocabularyLabel(this.appConfig.config()?.vocabularies.fuelTypes, this.car.value()?.fuelType, this.i18n.isArabic()),
  );
  protected readonly carType = computed(() => {
    const type = this.car.value()?.carType;
    return type ? (this.i18n.isArabic() ? type.nameAr || type.nameEn : type.nameEn || type.nameAr) : '';
  });

  protected readonly formValue = computed<SearchFormValue>(() => ({
    city: null,
    from: this.period()?.from ?? null,
    to: this.period()?.to ?? null,
    text: null,
  }));

  protected readonly bookingParams = computed(() => {
    const period = this.period();
    return searchToParams({ ...EMPTY_SEARCH, from: period?.from ?? null, to: period?.to ?? null }) as Record<string, string>;
  });

  constructor() {
    const setStatus = injectResponseStatus();
    const seo = inject(SeoService);

    effect(() => {
      if (this.notFound()) {
        setStatus(404);
        seo.set({ title: this.i18n.t('seo.notFound.title'), noindex: true });
        return;
      }
      if (this.problem()) setStatus(503);
      const car = this.car.value();
      if (!car) return;

      const canonical = slugFor(car.vehicleId, car.make, car.model, car.year);
      // An old or mistyped slug: settle on the one address this car has, keeping the dates.
      if (this.isBrowser && this.slug().toLowerCase() !== canonical) {
        void this.router.navigate(this.i18n.link('cars', canonical), { queryParams: this.params(), replaceUrl: true });
      }

      const name = `${car.make} ${car.model} ${car.year}`;
      const city = this.lookups.cityName(car.gallery.cityId);
      seo.set({
        title: city ? this.i18n.t('seo.car.title', { name, city }) : this.i18n.t('seo.car.titleNoCity', { name }),
        description: this.i18n.t('seo.car.description', {
          name,
          office: car.gallery.businessName,
          rate: this.format.money(car.dailyRate),
        }),
        path: `cars/${canonical}`,
        image: car.imageUrls[0] ?? null,
        type: 'product',
        // Pages reached with dates are the same car; the canonical is the date-less address.
        noindex: false,
        structuredData: [
          {
            '@context': 'https://schema.org',
            '@type': 'Car',
            name,
            brand: { '@type': 'Brand', name: car.make },
            model: car.model,
            vehicleModelDate: String(car.year),
            vehicleSeatingCapacity: car.seats,
            fuelType: car.fuelType,
            vehicleTransmission: car.transmission,
            ...(car.color ? { color: car.color } : {}),
            ...(car.imageUrls.length ? { image: car.imageUrls.map((url) => seo.absoluteAsset(url)) } : {}),
            offers: {
              '@type': 'Offer',
              price: car.dailyRate.amount,
              priceCurrency: car.dailyRate.currency,
              priceSpecification: {
                '@type': 'UnitPriceSpecification',
                price: car.dailyRate.amount,
                priceCurrency: car.dailyRate.currency,
                unitCode: 'DAY',
              },
              seller: { '@type': 'AutoRental', name: car.gallery.businessName },
            },
          },
        ],
      });
    });

    const shortlist = inject(ShortlistService);
    effect(() => {
      const id = this.car.value()?.vehicleId;
      if (id) void shortlist.track([id]);
    });
  }

  protected fuelPolicy(name: string): string {
    if (name === 'FullToFull') return this.i18n.t('car.fuelPolicy.FullToFull');
    if (name === 'SameToSame') return this.i18n.t('car.fuelPolicy.SameToSame');
    return name;
  }

  protected choose(value: SearchFormValue): void {
    void this.router.navigate([], {
      queryParams: searchToParams({ ...EMPTY_SEARCH, from: value.from, to: value.to }),
      replaceUrl: true,
    });
  }

  protected percent(value: number): string {
    return `${this.format.number(value)}%`;
  }
}
