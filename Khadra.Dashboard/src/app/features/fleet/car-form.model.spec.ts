import { describe, expect, it } from 'vitest';
import { CarFormValue, NEW_CAR, completeCarRequest, numberOrNull } from './car-form.model';

/** Pre-launch item 105: a new car starts with nothing the console chose, and nothing is sent until it is all stated. */
describe('the car form', () => {
  const answered: CarFormValue = {
    ...NEW_CAR,
    carTypeId: 'type-1',
    make: 'Kia',
    model: 'Rio',
    plateNumber: '12-34567',
    year: 2024,
    seats: 5,
    transmission: 'Automatic',
    fuelType: 'Petrol',
    dailyRate: 28,
    securityDeposit: 100,
  };

  it('starts a new car with every fact about it unanswered', () => {
    expect([NEW_CAR.year, NEW_CAR.seats, NEW_CAR.dailyRate, NEW_CAR.securityDeposit]).toEqual([
      null,
      null,
      null,
      null,
    ]);
    expect([NEW_CAR.transmission, NEW_CAR.fuelType]).toEqual(['', '']);
    expect(completeCarRequest(NEW_CAR)).toBeNull();
  });

  it('sends nothing while any one fact is unanswered', () => {
    for (const missing of ['year', 'seats', 'dailyRate', 'securityDeposit'] as const) {
      expect(completeCarRequest({ ...answered, [missing]: null })).toBeNull();
    }
    expect(completeCarRequest({ ...answered, transmission: '' })).toBeNull();
    expect(completeCarRequest({ ...answered, fuelType: '' })).toBeNull();
  });

  it('sends what the dealer stated, a zero deposit included', () => {
    expect(completeCarRequest(answered)).toEqual(answered);
    expect(completeCarRequest({ ...answered, securityDeposit: 0 })?.securityDeposit).toBe(0);
  });

  it('reads an empty box as unanswered, never as 0', () => {
    expect(numberOrNull('')).toBeNull();
    expect(numberOrNull('  ')).toBeNull();
    expect(numberOrNull('0')).toBe(0);
    expect(numberOrNull('27.5')).toBe(27.5);
  });
});
