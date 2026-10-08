import { VehicleRequest } from '../../core/models/fleet.api';

/**
 * The car form's working copy of a vehicle (pre-launch item 105).
 *
 * The form used to start a new car at 5 seats, 30 a day, a 150 deposit, automatic, petrol and this year. The vehicle
 * wizard had the same defaults and lost them, because a dealer who tabbed past them published a real car at figures
 * the console invented; this form kept them. So the facts a dealer must state about THIS car are nullable here, or
 * empty for a choice, and nothing is sent until each has been answered. `VehicleRequest` keeps them required, because
 * by the time anything is sent they have been.
 */
export type CarFormValue = Omit<
  VehicleRequest,
  'year' | 'seats' | 'dailyRate' | 'securityDeposit'
> & {
  readonly year: number | null;
  readonly seats: number | null;
  readonly dailyRate: number | null;
  readonly securityDeposit: number | null;
};

/** A new car: every fact about it unanswered. The policies keep the defaults the wizard keeps. */
export const NEW_CAR: CarFormValue = {
  carTypeId: '',
  make: '',
  model: '',
  year: null,
  color: null,
  seats: null,
  transmission: '',
  fuelType: '',
  description: { ar: null, en: null },
  plateNumber: '',
  dailyRate: null,
  securityDeposit: null,
  isDeliveryEligible: false,
  mileageUnlimited: true,
  mileageDailyLimitKm: null,
  mileageExcessFeePerKm: null,
  fuelPolicy: 'FullToFull',
};

/** The request, once every fact has been stated; null while any is still unanswered. */
export function completeCarRequest(form: CarFormValue): VehicleRequest | null {
  const { year, seats, dailyRate, securityDeposit } = form;
  if (year === null || seats === null || dailyRate === null || securityDeposit === null)
    return null;
  if (!form.transmission || !form.fuelType) return null;
  return { ...form, year, seats, dailyRate, securityDeposit };
}

/** A number box's value: null when it is empty, so an empty box is "not answered" and never 0. */
export function numberOrNull(raw: string): number | null {
  if (raw.trim() === '') return null;
  const value = Number(raw);
  return Number.isFinite(value) ? value : null;
}
