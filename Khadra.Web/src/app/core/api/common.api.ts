/** Shapes several endpoints share. Hand-written to the API's DTOs. */

/** `MoneyDto`: an amount and the currency it is in. Always printed with its code. */
export interface Money {
  readonly amount: number;
  readonly currency: string;
}

/**
 * `ResolvedTextDto`: something a rental office wrote, in the language the server could offer. The
 * language may not be the one asked for — the server shows what the office actually wrote rather than
 * an empty heading — so a page must render it in the language it carries, not the page's.
 */
export interface ResolvedText {
  readonly text: string;
  readonly language: string;
}

/** `PagedResult<T>`. */
export interface Paged<T> {
  readonly items: readonly T[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totalPages: number;
  readonly hasPrevious: boolean;
  readonly hasNext: boolean;
}

/** `LookupEntryDto` (cities, car types). */
export interface LookupEntry {
  readonly id: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly isActive: boolean;
  readonly displayOrder: number;
  readonly centreLatitude?: number | null;
  readonly centreLongitude?: number | null;
}

/** The name in the reader's language, falling back to the other when an administrator left one blank. */
export function lookupName(entry: { nameEn: string; nameAr: string } | null | undefined, arabic: boolean): string {
  if (!entry) return '';
  return (arabic ? entry.nameAr : entry.nameEn) || entry.nameEn || entry.nameAr;
}
