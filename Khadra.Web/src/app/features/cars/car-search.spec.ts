import { describe, expect, it } from 'vitest';
import { EMPTY_SEARCH, activeFilterCount, parseSearch, searchToApi, searchToParams } from './car-search';

const CITY = '3f2b8c1d-0e9f-4a7b-8c5d-9a7b6c5d4e3f';

describe('car search', () => {
  it('reads a shared URL back into the same search', () => {
    const params = {
      city: CITY,
      from: '2026-09-25T10:00',
      to: '2026-09-28T10:00',
      q: 'corolla',
      fuel: 'Hybrid',
      seats: '5',
      minPrice: '20',
      maxPrice: '45.5',
      delivery: '1',
      sort: 'PriceLowToHigh',
      page: '2',
    };
    expect(searchToParams(parseSearch(params))).toEqual(params);
  });

  it('keeps defaults out of the URL', () => {
    expect(searchToParams(EMPTY_SEARCH)).toEqual({});
    expect(searchToParams({ ...EMPTY_SEARCH, sort: 'Newest', page: 1 })).toEqual({});
  });

  it('drops anything malformed instead of passing it on', () => {
    const search = parseSearch({
      city: 'not-a-guid',
      from: '2026-09-25 10:00',
      to: '2026-09-28T10:00',
      seats: '-3',
      minPrice: 'cheap',
      sort: 'Random',
      page: '0',
      fuel: "Petrol'--",
    });
    expect(search).toEqual(EMPTY_SEARCH);
  });

  it('never keeps half a period', () => {
    const search = parseSearch({ from: '2026-09-25T10:00' });
    expect(search.from).toBeNull();
    expect(search.to).toBeNull();
  });

  it('sends the API instants in the platform zone, not wall-clock text', () => {
    const search = parseSearch({ from: '2026-09-25T10:00', to: '2026-09-28T09:30', city: CITY, delivery: '1' });
    expect(searchToApi(search, 'Asia/Amman')).toEqual({
      page: '1',
      pageSize: '12',
      pickupAt: '2026-09-25T07:00:00.000Z',
      returnAt: '2026-09-28T06:30:00.000Z',
      cityId: CITY,
      deliveryOnly: 'true',
    });
  });

  it('cannot price dates before it knows the zone', () => {
    const search = parseSearch({ from: '2026-09-25T10:00', to: '2026-09-28T09:30' });
    expect(searchToApi(search, null)).toBeNull();
    expect(searchToApi(EMPTY_SEARCH, null)).toEqual({ page: '1', pageSize: '12' });
  });

  it('counts narrowing filters but not the city, the dates or the order', () => {
    expect(activeFilterCount(parseSearch({ city: CITY, from: '2026-09-25T10:00', to: '2026-09-28T10:00', sort: 'YearNewest' }))).toBe(0);
    expect(activeFilterCount(parseSearch({ fuel: 'Hybrid', delivery: '1', minPrice: '10' }))).toBe(3);
  });
});
