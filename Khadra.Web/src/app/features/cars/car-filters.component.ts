import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { AppConfigService } from '../../core/config/app-config.service';
import { CatalogueFacets } from '../../core/api/catalogue.api';
import { LookupsService } from '../../core/api/lookups.service';
import { vocabularyLabel } from '../../core/api/app-config.api';
import { I18nService } from '../../core/i18n/i18n.service';
import { CarSearch } from './car-search';

/**
 * The narrowing filters. Every choice is built from data: car types from the lookup intersected with
 * what is listed, brands, fuels, years and seat counts from the catalogue's facets, transmission labels
 * from `/app-config`. A value no listed car has is never offered.
 *
 * Emits a partial search; the page puts it in the URL.
 */
@Component({
  selector: 'kh-car-filters',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './car-filters.component.html',
})
export class CarFiltersComponent {
  protected readonly i18n = inject(I18nService);
  private readonly lookups = inject(LookupsService);
  private readonly appConfig = inject(AppConfigService);

  readonly search = input.required<CarSearch>();
  readonly facets = input<CatalogueFacets | null>(null);
  readonly changed = output<Partial<CarSearch>>();

  protected readonly types = computed(() => {
    const present = new Set(this.facets()?.carTypeIds ?? []);
    return this.lookups.activeCarTypes().filter((type) => present.has(type.id));
  });

  protected readonly transmissions = computed(() => this.appConfig.config()?.vocabularies.transmissions ?? []);

  protected readonly fuels = computed(() => {
    const config = this.appConfig.config();
    return (this.facets()?.fuelTypes ?? []).map((name) => ({
      name,
      label: vocabularyLabel(config?.vocabularies.fuelTypes, name, this.i18n.isArabic()),
    }));
  });

  protected readonly priceInverted = computed(() => {
    const search = this.search();
    return search.minPrice !== null && search.maxPrice !== null && search.minPrice > search.maxPrice;
  });

  protected label(entry: { labelEn: string; labelAr: string; name: string }): string {
    return (this.i18n.isArabic() ? entry.labelAr : entry.labelEn) || entry.labelEn || entry.name;
  }

  protected typeName(entry: { nameEn: string; nameAr: string }): string {
    return (this.i18n.isArabic() ? entry.nameAr : entry.nameEn) || entry.nameEn || entry.nameAr;
  }

  protected setText(key: 'type' | 'make' | 'fuel' | 'transmission', value: string): void {
    this.changed.emit({ [key]: value === '' ? null : value });
  }

  protected setNumber(key: 'seats' | 'minPrice' | 'maxPrice' | 'minYear' | 'maxYear', value: string): void {
    const parsed = value.trim() === '' ? null : Number(value);
    this.changed.emit({ [key]: parsed !== null && Number.isFinite(parsed) && parsed >= 0 ? parsed : null });
  }

  protected setDelivery(value: boolean): void {
    this.changed.emit({ delivery: value });
  }
}
