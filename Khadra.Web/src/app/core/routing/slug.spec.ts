import { describe, expect, it } from 'vitest';
import { idFromSlug, slugFor, slugWords } from './slug';

const ID = '3f2b8c1d-0e9f-4a7b-8c5d-9a7b6c5d4e3f';

describe('slugs', () => {
  it('reads make, model and year into words', () => {
    expect(slugFor(ID, 'Toyota', 'Corolla', 2024)).toBe(`toyota-corolla-2024-${ID}`);
    expect(slugWords('Mercedes-Benz', 'C 200', 2023)).toBe('mercedes-benz-c-200-2023');
  });

  it('drops accents and anything that is not a Latin letter or digit', () => {
    expect(slugWords('Škoda', 'Octavia RS!', 2022)).toBe('skoda-octavia-rs-2022');
  });

  it('is the id alone when a name is written only in Arabic', () => {
    expect(slugFor(ID, 'مكتب النخبة لتأجير السيارات')).toBe(ID);
  });

  it('keeps the words for the Latin half of a mixed name', () => {
    expect(slugFor(ID, 'Al Nukhba مكتب')).toBe(`al-nukhba-${ID}`);
  });

  it('finds the id however the words have changed', () => {
    expect(idFromSlug(`toyota-corolla-2024-${ID}`)).toBe(ID);
    expect(idFromSlug(`some-old-words-${ID.toUpperCase()}`)).toBe(ID);
    expect(idFromSlug(ID)).toBe(ID);
  });

  it('finds nothing in a slug without an id', () => {
    expect(idFromSlug('toyota-corolla')).toBeNull();
    expect(idFromSlug('')).toBeNull();
    expect(idFromSlug(null)).toBeNull();
  });

  it('never grows without bound', () => {
    expect(slugWords('a'.repeat(200)).length).toBeLessThanOrEqual(80);
  });
});
