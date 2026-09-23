import { httpResource } from '@angular/common/http';
import { Injectable, computed, inject } from '@angular/core';
import { I18nService } from '../i18n/i18n.service';
import { LookupEntry, lookupName } from './common.api';

/**
 * Cities and car types: the administrator's lookup lists, asked for once and shared by every page
 * that names one. Only active entries are offered as choices; a retired one still names itself where
 * a record carries it.
 */
@Injectable({ providedIn: 'root' })
export class LookupsService {
  private readonly i18n = inject(I18nService);

  readonly cities = httpResource<LookupEntry[]>(() => '/api/v1/cities');
  readonly carTypes = httpResource<LookupEntry[]>(() => '/api/v1/car-types');

  readonly activeCities = computed(() => sortByOrder((this.cities.value() ?? []).filter((city) => city.isActive)));
  readonly activeCarTypes = computed(() => sortByOrder((this.carTypes.value() ?? []).filter((type) => type.isActive)));

  cityName(id: string | null | undefined): string {
    if (!id) return '';
    return lookupName(this.cities.value()?.find((city) => city.id === id), this.i18n.isArabic());
  }

  carTypeName(id: string | null | undefined): string {
    if (!id) return '';
    return lookupName(this.carTypes.value()?.find((type) => type.id === id), this.i18n.isArabic());
  }
}

function sortByOrder(entries: LookupEntry[]): LookupEntry[] {
  return [...entries].sort((a, b) => a.displayOrder - b.displayOrder || a.nameEn.localeCompare(b.nameEn));
}
