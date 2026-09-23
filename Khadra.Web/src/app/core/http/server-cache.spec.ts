import { beforeEach, describe, expect, it } from 'vitest';
import {
  SERVER_CACHE_TTL_MS,
  clearServerCache,
  readServerCache,
  serverCacheKey,
  writeServerCache,
} from './server-cache';

describe('server cache', () => {
  beforeEach(() => clearServerCache());

  it('holds only the three public lookups, per language', () => {
    expect(serverCacheKey('/api/v1/cities', 'ar')).toBe('ar|/api/v1/cities');
    expect(serverCacheKey('/api/v1/cities', 'en')).not.toBe(serverCacheKey('/api/v1/cities', 'ar'));
    expect(serverCacheKey('/api/v1/app-config', 'en')).not.toBeNull();
    expect(serverCacheKey('/api/v1/car-types', 'en')).not.toBeNull();
  });

  it('never holds availability, prices, a quote or anything with a query', () => {
    expect(serverCacheKey('/api/v1/vehicles', 'ar')).toBeNull();
    expect(serverCacheKey('/api/v1/vehicles?page=1', 'ar')).toBeNull();
    expect(serverCacheKey('/api/v1/vehicles/1/quote', 'ar')).toBeNull();
    expect(serverCacheKey('/api/v1/cities?x=1', 'ar')).toBeNull();
    expect(serverCacheKey('/api/v1/bookings', 'ar')).toBeNull();
  });

  it('forgets an entry once it is a minute old', () => {
    writeServerCache('ar|/api/v1/cities', ['x'], 1_000);
    expect(readServerCache('ar|/api/v1/cities', 1_000 + SERVER_CACHE_TTL_MS)).toEqual(['x']);
    expect(readServerCache('ar|/api/v1/cities', 1_001 + SERVER_CACHE_TTL_MS)).toBeUndefined();
  });
});
