import { describe, expect, it } from 'vitest';
import {
  DIRECTORY_TEXT_MAX,
  EMPTY_DIRECTORY_SEARCH,
  directoryFromParams,
  directoryToApi,
  directoryToParams,
  isNarrowed,
} from './directory-search';

const CITY = '01A0CB98-D42F-7E51-B900-F08AFA78D7B5';

describe('the office directory search', () => {
  it('reads name, city, delivery and page from the URL', () => {
    expect(directoryFromParams({ q: '  Arabiat ', city: CITY, delivery: '1', page: '3' })).toEqual({
      text: 'Arabiat',
      city: CITY.toLowerCase(),
      delivery: true,
      page: 3,
    });
  });

  it('treats anything malformed as no filter at all', () => {
    expect(directoryFromParams({ city: 'amman', delivery: 'yes', page: '-2' })).toEqual(EMPTY_DIRECTORY_SEARCH);
    expect(directoryFromParams({ page: '1.5' }).page).toBe(1);
  });

  it('never sends the API a name longer than any office can have', () => {
    expect(directoryFromParams({ q: 'x'.repeat(400) }).text).toHaveLength(DIRECTORY_TEXT_MAX);
  });

  it('writes a plain directory as a plain address', () => {
    expect(directoryToParams(EMPTY_DIRECTORY_SEARCH)).toEqual({});
    expect(directoryToParams({ text: 'car', city: null, delivery: true, page: 2 })).toEqual({ q: 'car', delivery: '1', page: '2' });
  });

  it('maps every filter onto the parameter GET /galleries takes', () => {
    expect(directoryToApi({ text: 'car', city: 'c', delivery: true, page: 2 }, 18)).toEqual({
      page: 2,
      pageSize: 18,
      text: 'car',
      cityId: 'c',
      deliveryOnly: true,
    });
    expect(directoryToApi(EMPTY_DIRECTORY_SEARCH, 18)).toEqual({ page: 1, pageSize: 18 });
  });

  it('round-trips through the URL', () => {
    const search = { text: 'Arabiat Car', city: CITY.toLowerCase(), delivery: true, page: 4 };
    expect(directoryFromParams(directoryToParams(search))).toEqual(search);
  });

  it('counts a name or delivery as narrowing, and a city alone as not', () => {
    expect(isNarrowed(EMPTY_DIRECTORY_SEARCH)).toBe(false);
    expect(isNarrowed({ ...EMPTY_DIRECTORY_SEARCH, city: 'c' })).toBe(false);
    expect(isNarrowed({ ...EMPTY_DIRECTORY_SEARCH, text: 'a' })).toBe(true);
    expect(isNarrowed({ ...EMPTY_DIRECTORY_SEARCH, delivery: true })).toBe(true);
  });
});
