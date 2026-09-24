import { CatalogueFacets, CatalogueListing } from '../../core/api/catalogue.api';
import { LookupEntry } from '../../core/api/common.api';

/** A listing the hero can show: only one with a real photo, never a placeholder in a showcase. */
export type ShowcaseListing = CatalogueListing & { readonly coverImageUrl: string };

/** The newest listings that have a photo, at most `limit`. Empty when none has one. */
export function heroShowcase(listings: readonly CatalogueListing[], limit: number): ShowcaseListing[] {
  return listings.filter((car): car is ShowcaseListing => !!car.coverImageUrl).slice(0, limit);
}

/** One vehicle-type card: the administrator's name for it and, when the facets report them, its figures. */
export interface HomeCategory {
  readonly id: string;
  readonly name: string;
  /** Null when the API did not report a count: the card then says nothing rather than a guess. */
  readonly vehicleCount: number | null;
  readonly coverImageUrl: string | null;
}

/**
 * The vehicle types worth offering: listed by the administrator AND held by at least one bookable car,
 * in the administrator's display order (the order /car-types already returns).
 *
 * The count and photo come from the facets' per-type summary. An API that predates it still yields
 * cards — names only — because `carTypeIds` has always been there.
 */
export function homeCategories(
  types: readonly LookupEntry[],
  facets: CatalogueFacets | undefined,
  arabic: boolean,
): HomeCategory[] {
  if (!facets) return [];
  const present = new Set(facets.carTypeIds);
  const summaries = new Map((facets.carTypes ?? []).map((summary) => [summary.carTypeId, summary]));
  return types
    .filter((type) => present.has(type.id))
    .map((type) => {
      const summary = summaries.get(type.id);
      return {
        id: type.id,
        name: arabic ? type.nameAr || type.nameEn : type.nameEn || type.nameAr,
        vehicleCount: summary?.listedVehicleCount ?? null,
        coverImageUrl: summary?.coverImageUrl ?? null,
      };
    })
    .filter((category) => category.vehicleCount !== 0);
}
