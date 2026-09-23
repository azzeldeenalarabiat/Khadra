import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { CatalogueFacets, CatalogueListing, PublicGalleryCard } from '../../core/api/catalogue.api';
import { Paged } from '../../core/api/common.api';
import { LookupsService } from '../../core/api/lookups.service';
import { ShortlistService } from '../../core/api/shortlist.service';
import { snapshotProblem } from '../../core/http/problem';
import { I18nService } from '../../core/i18n/i18n.service';
import { SeoService } from '../../core/seo/seo.service';
import { CarCardComponent } from '../../shared/car-card/car-card.component';
import { IconComponent } from '../../shared/icon/icon.component';
import { OfficeCardComponent } from '../../shared/office-card/office-card.component';
import { SearchFormComponent, SearchFormValue } from '../../shared/search-form/search-form.component';
import { StatePanelComponent } from '../../shared/state/state-panel.component';
import { EMPTY_SEARCH, searchToParams } from '../cars/car-search';

/** How many cars and offices the home page shows before "view all". Layout, not a business rule. */
const HOME_CARS = 8;
const HOME_OFFICES = 6;

/**
 * The home page is a search first. Below it: the car types the catalogue actually holds, the most
 * recently listed cars, and rental offices — each from its own API, each with its own loading, empty
 * and error state. Nothing on it is "popular" or "featured": the platform keeps no such figure.
 */
@Component({
  selector: 'kh-home',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, SearchFormComponent, CarCardComponent, OfficeCardComponent, StatePanelComponent, IconComponent],
  templateUrl: './home.component.html',
})
export class HomeComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly lookups = inject(LookupsService);
  private readonly router = inject(Router);

  protected readonly cars = httpResource<Paged<CatalogueListing>>(() => ({
    url: '/api/v1/vehicles',
    params: { page: 1, pageSize: HOME_CARS },
  }));
  protected readonly offices = httpResource<Paged<PublicGalleryCard>>(() => ({
    url: '/api/v1/galleries',
    params: { page: 1, pageSize: HOME_OFFICES },
  }));
  protected readonly facets = httpResource<CatalogueFacets>(() => '/api/v1/vehicles/facets');

  protected readonly carsProblem = computed(() => (this.cars.error() ? snapshotProblem(this.cars.error()) : null));
  protected readonly officesProblem = computed(() => (this.offices.error() ? snapshotProblem(this.offices.error()) : null));

  /** A type is offered only when the administrator lists it AND a bookable car has it. */
  protected readonly types = computed(() => {
    const present = new Set(this.facets.value()?.carTypeIds ?? []);
    return this.lookups.activeCarTypes().filter((type) => present.has(type.id));
  });

  protected readonly skeletons = Array.from({ length: 4 }, (_, index) => index);

  constructor() {
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

  protected search(value: SearchFormValue): void {
    void this.router.navigate(this.i18n.link('cars'), {
      queryParams: searchToParams({ ...EMPTY_SEARCH, city: value.city, from: value.from, to: value.to, text: value.text }),
    });
  }
}
