import { describe, expect, it } from 'vitest';
import { AR } from './ar';
import { EN } from './en';
import { I18nService } from './i18n.service';

describe('switchedUrl', () => {
  it('swaps the language segment and keeps the rest', () => {
    expect(I18nService.switchedUrl('/ar/cars?city=x&page=2', 'en')).toBe('/en/cars?city=x&page=2');
    expect(I18nService.switchedUrl('/en/dealers/abc#hours', 'ar')).toBe('/ar/dealers/abc#hours');
    expect(I18nService.switchedUrl('/ar', 'en')).toBe('/en');
  });

  it('adds a language to a path that has none', () => {
    expect(I18nService.switchedUrl('/', 'en')).toBe('/en');
    expect(I18nService.switchedUrl('/cars', 'ar')).toBe('/ar/cars');
  });
});

describe('dictionaries', () => {
  it('Arabic has every English key and nothing else', () => {
    expect(Object.keys(AR).sort()).toEqual(Object.keys(EN).sort());
  });

  it('keeps the same placeholders in both languages', () => {
    const placeholders = (message: unknown) =>
      [...new Set(JSON.stringify(message).match(/\{\w+\}/g) ?? [])].sort();
    for (const key of Object.keys(EN) as (keyof typeof EN)[]) {
      const english = placeholders(EN[key]).filter((p) => p !== '{count}');
      const arabic = placeholders(AR[key]).filter((p) => p !== '{count}');
      expect(arabic, key).toEqual(english);
    }
  });

  it('gives every Arabic plural the forms Arabic needs', () => {
    for (const [key, message] of Object.entries(AR)) {
      if (typeof message === 'string') continue;
      expect(Object.keys(message).sort(), key).toEqual(['few', 'many', 'one', 'other', 'two', 'zero'].sort());
    }
  });
});
