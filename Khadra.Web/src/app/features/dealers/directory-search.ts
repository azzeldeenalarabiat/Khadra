/**
 * The rental-office directory's search, as it lives in the URL: `?q=&city=&delivery=1&page=`.
 *
 * Its own search, deliberately separate from the car search: an office is looked for by its name and
 * where it is, a car by dates and what it is. Every filter maps to a parameter `GET /api/v1/galleries`
 * actually takes (`text`, `cityId`, `deliveryOnly`) — nothing is filtered in the browser, so the count
 * and the pages are the server's.
 */
export interface DirectorySearch {
  readonly text: string;
  readonly city: string | null;
  readonly delivery: boolean;
  readonly page: number;
}

export const EMPTY_DIRECTORY_SEARCH: DirectorySearch = { text: '', city: null, delivery: false, page: 1 };

/** The API refuses nothing longer, and no office name is: `BusinessName.MaxLength`. */
export const DIRECTORY_TEXT_MAX = 150;

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function one(value: unknown): string | undefined {
  return Array.isArray(value) ? (typeof value[0] === 'string' ? value[0] : undefined) : typeof value === 'string' ? value : undefined;
}

/** Reads the URL leniently: anything malformed is simply not a filter. */
export function directoryFromParams(params: Record<string, unknown>): DirectorySearch {
  const text = (one(params['q']) ?? '').trim().slice(0, DIRECTORY_TEXT_MAX);
  const city = one(params['city']);
  const page = Number(one(params['page']));
  return {
    text,
    city: city && GUID.test(city) ? city.toLowerCase() : null,
    delivery: one(params['delivery']) === '1',
    page: Number.isInteger(page) && page > 1 ? page : 1,
  };
}

/** Back to the URL, leaving out every default so a plain directory has a plain address. */
export function directoryToParams(search: DirectorySearch): Record<string, string> {
  return {
    ...(search.text ? { q: search.text } : {}),
    ...(search.city ? { city: search.city } : {}),
    ...(search.delivery ? { delivery: '1' } : {}),
    ...(search.page > 1 ? { page: String(search.page) } : {}),
  };
}

/** The API's own names for the same filters. */
export function directoryToApi(search: DirectorySearch, pageSize: number): Record<string, string | number | boolean> {
  return {
    page: search.page,
    pageSize,
    ...(search.text ? { text: search.text } : {}),
    ...(search.city ? { cityId: search.city } : {}),
    ...(search.delivery ? { deliveryOnly: true } : {}),
  };
}

/** A name search or the delivery filter narrows the list; a city is a place, not a narrowing. */
export function isNarrowed(search: DirectorySearch): boolean {
  return search.text !== '' || search.delivery;
}
