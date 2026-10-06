import { beforeEach, describe, expect, it } from 'vitest';

import { forgetReturnAddress, recallReturnAddress, rememberReturnAddress, withSaveIntent } from './return-address';

const CAR = '01a0cb9c-6bef-7dc5-bfe6-3cc9fc12702c';

describe('withSaveIntent (E2E F12)', () => {
  it('adds the car to save to a page without a query, and keeps any query it had', () => {
    expect(withSaveIntent('/en/cars', CAR)).toBe(`/en/cars?save=${CAR}`);
    expect(withSaveIntent('/ar/cars?city=amman', CAR)).toBe(`/ar/cars?city=amman&save=${CAR}`);
    expect(withSaveIntent(`/en/cars?save=other`, CAR)).toBe(`/en/cars?save=${CAR}`);
  });
});

describe('the remembered return address', () => {
  beforeEach(() => localStorage.clear());

  it('is followed across the verification step while it is fresh', () => {
    rememberReturnAddress(localStorage, `/en/cars?save=${CAR}`, 1_000);
    expect(recallReturnAddress(localStorage, 'en', 1_000 + 60_000)).toBe(`/en/cars?save=${CAR}`);
  });

  it('is not followed once a day old, nor when it is not a path on this site', () => {
    rememberReturnAddress(localStorage, '/en/cars', 0);
    expect(recallReturnAddress(localStorage, 'en', 24 * 60 * 60 * 1000 + 1)).toBeNull();

    rememberReturnAddress(localStorage, '//evil.example/steal', Date.now());
    expect(recallReturnAddress(localStorage, 'en')).toBeNull();
  });

  it('is forgotten once used, and never breaks a browser that refuses storage', () => {
    rememberReturnAddress(localStorage, '/en/cars');
    forgetReturnAddress(localStorage);
    expect(recallReturnAddress(localStorage, 'en')).toBeNull();

    const refusing = { setItem: () => { throw new Error('denied'); }, getItem: () => { throw new Error('denied'); }, removeItem: () => { throw new Error('denied'); } } as unknown as Storage;
    expect(() => rememberReturnAddress(refusing, '/en/cars')).not.toThrow();
    expect(recallReturnAddress(refusing, 'en')).toBeNull();
    expect(() => forgetReturnAddress(refusing)).not.toThrow();
  });
});
