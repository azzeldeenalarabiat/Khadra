import { Params } from '@angular/router';
import { WallClock, wallClockToInstant } from '../../core/i18n/zoned-time';

/**
 * A car search, exactly as its URL spells it — so a search can be bookmarked, shared, crawled and
 * reloaded, and the back button walks through the filters a customer tried.
 *
 * Dates live in the URL as the WALL-CLOCK time at the rental office (`from=2026-09-25T10:00`), which
 * is what a person reads and types. They become instants only on the way to the API, in the platform's
 * zone from `/app-config`.
 *
 * Every filter here is one the API supports. None is offered that the server would ignore.
 */
export interface CarSearch {
  readonly city: string | null;
  readonly type: string | null;
  readonly from: WallClock | null;
  readonly to: WallClock | null;
  readonly text: string | null;
  readonly make: string | null;
  readonly fuel: string | null;
  readonly transmission: string | null;
  readonly seats: number | null;
  readonly minPrice: number | null;
  readonly maxPrice: number | null;
  readonly minYear: number | null;
  readonly maxYear: number | null;
  readonly delivery: boolean;
  readonly dealer: string | null;
  readonly sort: CarSort;
  readonly page: number;
}

export const CAR_SORTS = ['Newest', 'PriceLowToHigh', 'PriceHighToLow', 'YearNewest'] as const;
export type CarSort = (typeof CAR_SORTS)[number];

export const PAGE_SIZE = 12;

export const EMPTY_SEARCH: CarSearch = {
  city: null,
  type: null,
  from: null,
  to: null,
  text: null,
  make: null,
  fuel: null,
  transmission: null,
  seats: null,
  minPrice: null,
  maxPrice: null,
  minYear: null,
  maxYear: null,
  delivery: false,
  dealer: null,
  sort: 'Newest',
  page: 1,
};

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const STAMP = /^(\d{4}-\d{2}-\d{2})T([01]\d|2[0-3]):([0-5]\d)$/;
const NAME = /^[A-Za-z][A-Za-z0-9]{0,40}$/;

export function parseSearch(params: Params): CarSearch {
  const from = stamp(params['from']);
  const to = stamp(params['to']);
  // Half a period means nothing to the API (it refuses one), so it means nothing here either.
  const period = from && to ? { from, to } : { from: null, to: null };
  const sort = CAR_SORTS.find((candidate) => candidate === params['sort']) ?? 'Newest';

  return {
    city: guid(params['city']),
    type: guid(params['type']),
    ...period,
    text: text(params['q']),
    make: text(params['make']),
    fuel: name(params['fuel']),
    transmission: name(params['transmission']),
    seats: whole(params['seats'], 1, 60),
    minPrice: amount(params['minPrice']),
    maxPrice: amount(params['maxPrice']),
    minYear: whole(params['minYear'], 1950, 2100),
    maxYear: whole(params['maxYear'], 1950, 2100),
    delivery: params['delivery'] === '1',
    dealer: guid(params['dealer']),
    sort,
    page: whole(params['page'], 1, 10_000) ?? 1,
  };
}

/** The URL query for a search, leaving out everything at its default so URLs stay short and canonical. */
export function searchToParams(search: CarSearch): Params {
  const params: Params = {};
  const set = (key: string, value: string | number | null | false) => {
    if (value !== null && value !== false && value !== '') params[key] = String(value);
  };
  set('city', search.city);
  set('type', search.type);
  if (search.from && search.to) {
    params['from'] = `${search.from.date}T${search.from.time}`;
    params['to'] = `${search.to.date}T${search.to.time}`;
  }
  set('q', search.text);
  set('make', search.make);
  set('fuel', search.fuel);
  set('transmission', search.transmission);
  set('seats', search.seats);
  set('minPrice', search.minPrice);
  set('maxPrice', search.maxPrice);
  set('minYear', search.minYear);
  set('maxYear', search.maxYear);
  if (search.delivery) params['delivery'] = '1';
  set('dealer', search.dealer);
  if (search.sort !== 'Newest') params['sort'] = search.sort;
  if (search.page > 1) params['page'] = String(search.page);
  return params;
}

/** The query `GET /api/v1/vehicles` takes. Null while dates are chosen but the zone is not yet known. */
export function searchToApi(search: CarSearch, timeZone: string | null): Record<string, string> | null {
  const api: Record<string, string> = { page: String(search.page), pageSize: String(PAGE_SIZE) };
  if (search.from && search.to) {
    if (!timeZone) return null;
    const pickup = wallClockToInstant(search.from, timeZone);
    const dropoff = wallClockToInstant(search.to, timeZone);
    if (!pickup || !dropoff) return null;
    api['pickupAt'] = pickup.toISOString();
    api['returnAt'] = dropoff.toISOString();
  }
  const set = (key: string, value: string | number | null) => {
    if (value !== null && value !== '') api[key] = String(value);
  };
  set('cityId', search.city);
  set('carTypeId', search.type);
  set('text', search.text);
  set('make', search.make);
  set('fuelType', search.fuel);
  set('transmission', search.transmission);
  set('minSeats', search.seats);
  set('minDailyRate', search.minPrice);
  set('maxDailyRate', search.maxPrice);
  set('minYear', search.minYear);
  set('maxYear', search.maxYear);
  set('dealerId', search.dealer);
  if (search.delivery) api['deliveryOnly'] = 'true';
  if (search.sort !== 'Newest') api['sort'] = search.sort;
  return api;
}

/** How many narrowing filters are set, for the "Filters (3)" button. Dates, city and sort are not filters. */
export function activeFilterCount(search: CarSearch): number {
  return [
    search.type,
    search.text,
    search.make,
    search.fuel,
    search.transmission,
    search.seats,
    search.minPrice,
    search.maxPrice,
    search.minYear,
    search.maxYear,
    search.delivery || null,
    search.dealer,
  ].filter((value) => value !== null && value !== '').length;
}

function stamp(value: unknown): WallClock | null {
  const match = typeof value === 'string' ? STAMP.exec(value) : null;
  return match ? { date: match[1]!, time: `${match[2]}:${match[3]}` } : null;
}

function guid(value: unknown): string | null {
  return typeof value === 'string' && GUID.test(value) ? value.toLowerCase() : null;
}

function name(value: unknown): string | null {
  return typeof value === 'string' && NAME.test(value) ? value : null;
}

function text(value: unknown): string | null {
  if (typeof value !== 'string') return null;
  const trimmed = value.trim().slice(0, 80);
  return trimmed === '' ? null : trimmed;
}

function whole(value: unknown, min: number, max: number): number | null {
  const parsed = typeof value === 'string' && /^\d+$/.test(value) ? Number(value) : NaN;
  return Number.isInteger(parsed) && parsed >= min && parsed <= max ? parsed : null;
}

function amount(value: unknown): number | null {
  const parsed = typeof value === 'string' && /^\d+(\.\d{1,3})?$/.test(value) ? Number(value) : NaN;
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : null;
}
