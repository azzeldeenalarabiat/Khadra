import { Injectable, computed, inject } from '@angular/core';
import { I18nService } from '../i18n/i18n.service';
import { LookupEntry, lookupName } from './common.api';
import { httpData } from '../http/http-data';

/**
 * Cities and car types: the administrator's lookup lists, asked for once and shared by every page
 * that names one. Only active entries are offered as choices; a retired one still names itself where
 * a record carries it.
 */
@Injectable({ providedIn: 'root' })
export class LookupsService {
  private readonly i18n = inject(I18nService);

  readonly cities = httpData<LookupEntry[]>(() => '/api/v1/cities');
  readonly carTypes = httpData<LookupEntry[]>(() => '/api/v1/car-types');

  readonly activeCities = computed(() => sortByOrder((this.cities.value() ?? []).filter((city) => city.isActive)));
  readonly activeCarTypes = computed(() => sortByOrder((this.carTypes.value() ?? []).filter((type) => type.isActive)));

  /**
   * The city list could not be loaded (pre-launch item 155). A search still sends the city it was given, so its
   * results stay right; what must not happen is a filtered search being headed "Any city", or a select offering
   * "Any city" as though it were the only choice. While this holds, every city filter says it cannot be shown.
   */
  readonly citiesUnavailable = computed(() => this.cities.error() != null);

  cityName(id: string | null | undefined): string {
    if (!id) return '';
    return lookupName(this.cities.value()?.find((city) => city.id === id), this.i18n.isArabic());
  }

  /**
   * What a city filter reads as: the city's name, "Any city" when there is no filter, and — while the list has
   * failed — that the chosen city cannot be named, never "Any city" for a search that IS filtered by one.
   */
  cityFilterLabel(id: string | null | undefined): string {
    if (id && this.citiesUnavailable()) return this.i18n.t('search.cityUnavailable');
    return this.cityName(id) || this.i18n.t('search.anyCity');
  }

  carTypeName(id: string | null | undefined): string {
    if (!id) return '';
    return lookupName(this.carTypes.value()?.find((type) => type.id === id), this.i18n.isArabic());
  }
}

function sortByOrder(entries: LookupEntry[]): LookupEntry[] {
  return [...entries].sort((a, b) => a.displayOrder - b.displayOrder || a.nameEn.localeCompare(b.nameEn));
}
