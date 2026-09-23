import { describe, expect, it } from 'vitest';
import { safeReturnUrl } from './auth.guards';

describe('safeReturnUrl', () => {
  it('follows a path on this site', () => {
    expect(safeReturnUrl('/ar/bookings/abc', 'en')).toBe('/ar/bookings/abc');
    expect(safeReturnUrl('/en/cars?city=x', 'ar')).toBe('/en/cars?city=x');
    expect(safeReturnUrl('/en', 'ar')).toBe('/en');
  });

  it('refuses anywhere else and goes home instead', () => {
    expect(safeReturnUrl('//evil.example/ar', 'ar')).toBe('/ar');
    expect(safeReturnUrl('https://evil.example', 'en')).toBe('/en');
    expect(safeReturnUrl('/\\evil.example', 'en')).toBe('/en');
    expect(safeReturnUrl('javascript:alert(1)', 'en')).toBe('/en');
    expect(safeReturnUrl('/admin', 'ar')).toBe('/ar');
    expect(safeReturnUrl(null, 'ar')).toBe('/ar');
  });
});
