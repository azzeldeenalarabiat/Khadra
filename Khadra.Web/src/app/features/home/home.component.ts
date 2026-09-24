import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { CatalogueFacets, CatalogueListing, PublicGalleryCard } from '../../core/api/catalogue.api';
import { Paged } from '../../core/api/common.api';
import { LookupsService } from '../../core/api/lookups.service';
import { ShortlistService } from '../../core/api/shortlist.service';
import { AppConfigService } from '../../core/config/app-config.service';
import { snapshotProblem } from '../../core/http/problem';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { slugFor } from '../../core/routing/slug';
import { SeoService } from '../../core/seo/seo.service';
import { CarCardComponent } from '../../shared/car-card/car-card.component';
import { IconComponent } from '../../shared/icon/icon.component';
import { OfficeCardComponent } from '../../shared/office-card/office-card.component';
import { SearchFormComponent, SearchFormValue } from '../../shared/search-form/search-form.component';
import { StatePanelComponent } from '../../shared/state/state-panel.component';
import { EMPTY_SEARCH, searchToParams } from '../cars/car-search';
import { httpData } from '../../core/http/http-data';
import { injectResponseStatus } from '../../core/http/server-context';
import { homeCategories, heroShowcase } from './home-view';

/** How many cars and offices the home page shows before "view all". Layout, not a business rule. */
const HOME_CARS = 8;
const HOME_OFFICES = 6;
/** How many listing photos the hero shows. Layout, not a business rule. */
const HERO_SHOTS = 3;

/**
 * The home page is a search first. Around it, everything is the catalogue's own: the hero's photos are
 * the newest real listings, the vehicle types are the ones a bookable car actually has (with the count
 * and a photo the facets report), the offices are the directory's first page, and the closing figures
 * are the two lists' totals. Nothing on it is "popular" or "featured": the platform keeps no such figure.
 */
@Component({
  selector: 'kh-home',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, SearchFormComponent, CarCardComponent, OfficeCardComponent, StatePanelComponent, IconComponent],
  templateUrl: './home.component.html',
})
export class HomeComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly format = inject(FormatService);
  private readonly lookups = inject(LookupsService);
  private readonly appConfig = inject(AppConfigService);
  private readonly router = inject(Router);

  protected readonly cars = httpData<Paged<CatalogueListing>>(() => ({
    url: '/api/v1/vehicles',
    params: { page: 1, pageSize: HOME_CARS },
  }));
  protected readonly offices = httpData<Paged<PublicGalleryCard>>(() => ({
    url: '/api/v1/galleries',
    params: { page: 1, pageSize: HOME_OFFICES },
  }));
  protected readonly facets = httpData<CatalogueFacets>(() => '/api/v1/vehicles/facets');

  protected readonly carsProblem = computed(() => (this.cars.error() ? snapshotProblem(this.cars.error()) : null));
  protected readonly officesProblem = computed(() => (this.offices.error() ? snapshotProblem(this.offices.error()) : null));

  protected readonly showcase = computed(() => heroShowcase(this.cars.value()?.items ?? [], HERO_SHOTS));

  protected readonly categories = computed(() =>
    homeCategories(this.lookups.activeCarTypes(), this.facets.value(), this.i18n.isArabic()),
  );

  /** From /app-config, so the promise on the page is the one the platform enforces. */
  protected readonly paymentWindowHours = computed(() => this.appConfig.config()?.paymentWindowHours ?? null);

  /** The closing line's figures: the two lists' own totals, shown only once both have answered. */
  protected readonly inventory = computed(() => {
    const cars = this.cars.value();
    const offices = this.offices.value();
    return cars && offices && cars.totalCount > 0 ? { cars: cars.totalCount, offices: offices.totalCount } : null;
  });

  protected readonly skeletons = Array.from({ length: 4 }, (_, index) => index);

  constructor() {
    // The cars are what this page is for: without them it is an outage, not a page to index.
    const setStatus = injectResponseStatus();
    effect(() => {
      if (this.carsProblem()) setStatus(503);
    });
    inject(SeoService).set({
      title: this.i18n.t('seo.home.title'),
      description: this.i18n.t('seo.home.description'),
      path: '',
      structuredData: [
        {
          '@context': 'https://schema.org',
          '@type': 'WebSite',
          name: this.i18n.t('brand.name'),
          inLanguage: this.i18n.language(),
        },
      ],
    });

    const shortlist = inject(ShortlistService);
    effect(() => void shortlist.track(this.cars.value()?.items.map((car) => car.vehicleId) ?? []));
  }

  protected carLink(car: CatalogueListing): (string | number)[] {
    return this.i18n.link('cars', slugFor(car.vehicleId, car.make, car.model, car.year));
  }

  protected search(value: SearchFormValue): void {
    void this.router.navigate(this.i18n.link('cars'), {
      queryParams: searchToParams({ ...EMPTY_SEARCH, city: value.city, from: value.from, to: value.to, text: value.text }),
    });
  }
}
