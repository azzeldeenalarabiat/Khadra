import { httpResource } from '@angular/common/http';
import { isPlatformServer } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  PLATFORM_ID,
  computed,
  effect,
  inject,
  viewChild,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { CatalogueFacets, CatalogueListing } from '../../core/api/catalogue.api';
import { Paged } from '../../core/api/common.api';
import { LookupsService } from '../../core/api/lookups.service';
import { ShortlistService } from '../../core/api/shortlist.service';
import { AppConfigService } from '../../core/config/app-config.service';
import { snapshotProblem } from '../../core/http/problem';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { wallClockToInstant } from '../../core/i18n/zoned-time';
import { SeoService } from '../../core/seo/seo.service';
import { CarCardComponent } from '../../shared/car-card/car-card.component';
import { IconComponent } from '../../shared/icon/icon.component';
import { SearchFormComponent, SearchFormValue } from '../../shared/search-form/search-form.component';
import { StatePanelComponent } from '../../shared/state/state-panel.component';
import { CarFiltersComponent } from './car-filters.component';
import {
  CAR_SORTS,
  CarSearch,
  CarSort,
  EMPTY_SEARCH,
  activeFilterCount,
  parseSearch,
  searchToApi,
  searchToParams,
} from './car-search';

/**
 * Search results. The URL is the search: every filter, the dates, the order and the page are query
 * parameters, so the back button, a bookmark and a shared link all reproduce it.
 *
 * On the server only a date-less search is rendered — availability for particular dates changes by the
 * minute and is nobody's to cache or index — so a search with dates renders its skeleton there and asks
 * the API from the browser.
 */
@Component({
  selector: 'kh-cars',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, SearchFormComponent, CarFiltersComponent, CarCardComponent, StatePanelComponent, IconComponent],
  templateUrl: './cars.component.html',
})
export class CarsComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly format = inject(FormatService);
  protected readonly lookups = inject(LookupsService);
  private readonly appConfig = inject(AppConfigService);
  private readonly router = inject(Router);
  private readonly isServer = isPlatformServer(inject(PLATFORM_ID));

  private readonly params = toSignal(inject(ActivatedRoute).queryParams, { initialValue: {} });
  protected readonly search = computed(() => parseSearch(this.params()));
  protected readonly sorts = CAR_SORTS;
  protected readonly filterCount = computed(() => activeFilterCount(this.search()));
  protected readonly hasDates = computed(() => this.search().from !== null);

  private readonly filtersDialog = viewChild<ElementRef<HTMLDialogElement>>('filtersDialog');

  protected readonly results = httpResource<Paged<CatalogueListing>>(() => {
    const search = this.search();
    if (this.isServer && search.from) return undefined;
    const params = searchToApi(search, this.format.timeZone());
    return params ? { url: '/api/v1/vehicles', params } : undefined;
  });
  protected readonly facets = httpResource<CatalogueFacets>(() => '/api/v1/vehicles/facets');

  protected readonly problem = computed(() => (this.results.error() ? snapshotProblem(this.results.error()) : null));

  /** A refusal the API explained (dates it will not search) is worded; anything else is a failure panel. */
  protected readonly refusal = computed(() => {
    const problem = this.problem();
    const config = this.appConfig.config();
    if (!problem || problem.status !== 400) return null;
    switch (problem.code) {
      case 'booking.too_soon':
      case 'booking.period_in_past': {
        if (!config) return this.i18n.t('search.returnBeforePickup');
        const earliest = new Date(Date.now() + config.minimumBookingLeadTimeMinutes * 60_000);
        return this.i18n.t('search.tooSoon', { when: this.format.dateTime(earliest) });
      }
      case 'booking.beyond_horizon':
        return config ? this.i18n.t('search.tooFar', { days: config.maxAdvanceBookingDays }) : null;
      case 'booking.rental_too_long':
        return config ? this.i18n.t('search.tooLong', { days: config.maxRentalDays }) : null;
      case 'period.end_before_start':
        return this.i18n.t('search.returnBeforePickup');
      case 'catalogue.incomplete_period':
        return this.i18n.t('search.datesIncomplete');
      default:
        return null;
    }
  });

  protected readonly periodLabel = computed(() => {
    const search = this.search();
    const zone = this.format.timeZone();
    if (!search.from || !search.to || !zone) return this.i18n.t('cars.anyDates');
    return this.i18n.t('cars.period', {
      from: this.format.dateTime(wallClockToInstant(search.from, zone)),
      to: this.format.dateTime(wallClockToInstant(search.to, zone)),
    });
  });

  protected readonly cityLabel = computed(() => this.lookups.cityName(this.search().city) || this.i18n.t('search.anyCity'));

  /** Dates travel to a car's page, so the price there is for the period the customer already chose. */
  protected readonly carry = computed(() => {
    const params = searchToParams({ ...EMPTY_SEARCH, from: this.search().from, to: this.search().to });
    return params as Record<string, string>;
  });

  protected readonly formValue = computed<SearchFormValue>(() => {
    const search = this.search();
    return { city: search.city, from: search.from, to: search.to, text: search.text };
  });

  protected readonly skeletons = Array.from({ length: 6 }, (_, index) => index);

  constructor() {
    const seo = inject(SeoService);
    effect(() => {
      const search = this.search();
      const city = this.lookups.cityName(search.city);
      // A city or car-type listing is a landing page worth indexing; a search with dates or finer
      // filters is one person's question, and is not.
      const indexable =
        !search.from && search.page === 1 && search.sort === 'Newest' &&
        activeFilterCount({ ...search, type: null }) === 0;
      const query = new URLSearchParams();
      if (search.city) query.set('city', search.city);
      if (search.type) query.set('type', search.type);
      seo.set({
        title: city ? this.i18n.t('seo.cars.titleIn', { city }) : this.i18n.t('seo.cars.title'),
        description: this.i18n.t('seo.cars.description'),
        path: `cars${query.size ? `?${query}` : ''}`,
        noindex: !indexable,
      });
    });

    const shortlist = inject(ShortlistService);
    effect(() => void shortlist.track(this.results.value()?.items.map((car) => car.vehicleId) ?? []));
  }

  protected update(patch: Partial<CarSearch>): void {
    // Any change to what is searched starts again at page one.
    const next = { ...this.search(), ...patch, page: 'page' in patch ? patch.page! : 1 };
    void this.router.navigate([], { queryParams: searchToParams(next) });
  }

  protected submitForm(value: SearchFormValue): void {
    this.update({ city: value.city, from: value.from, to: value.to, text: value.text });
  }

  protected setSort(value: string): void {
    const sort = CAR_SORTS.find((candidate) => candidate === value) ?? 'Newest';
    this.update({ sort: sort as CarSort });
  }

  protected clearFilters(): void {
    const search = this.search();
    this.update({ ...EMPTY_SEARCH, city: search.city, from: search.from, to: search.to, sort: search.sort });
  }

  protected pageParams(page: number): Record<string, string> {
    return searchToParams({ ...this.search(), page }) as Record<string, string>;
  }

  protected openFilters(): void {
    this.filtersDialog()?.nativeElement.showModal();
  }

  protected closeFilters(): void {
    this.filtersDialog()?.nativeElement.close();
  }
}
