/**
 * The wire shape of the dealer fleet endpoints (spec 4.3).
 *
 * `status` is what the dealer controls; `isBookable` is the server's answer to "would a customer see
 * this", which also depends on the dealer's own approval state. Both are sent because a listing can
 * be Active and still not bookable — a suspended dealer's cars, for instance — and the screen has to
 * be able to say so rather than implying the car is live.
 *
 * There is no "Booked" status and never will be: whether a car is free on given dates is a question
 * about bookings, not a flag on the car.
 */
export interface Money {
  readonly amount: number;
  readonly currency: string;
}

export interface MileagePolicy {
  readonly isUnlimited: boolean;
  readonly dailyLimitKm: number | null;
  readonly excessFeePerKm: Money | null;
}

export interface VehicleImage {
  readonly imageId: string;
  /** A public path, unlike the expiring signed links used for identity documents (spec 7). */
  readonly url: string;
  readonly position: number;
  readonly isPrimary: boolean;
}

export interface Vehicle {
  readonly vehicleId: string;
  readonly carTypeId: string;
  readonly make: string;
  readonly model: string;
  readonly year: number;
  readonly color: string | null;
  readonly seats: number;
  readonly transmission: string;
  readonly fuelType: string;
  readonly description: string | null;
  readonly plateNumber: string;
  readonly dailyRate: Money;
  readonly securityDeposit: Money;
  readonly isDeliveryEligible: boolean;
  readonly mileage: MileagePolicy;
  readonly fuelPolicy: string;
  readonly status: 'Draft' | 'Active' | 'Hidden' | 'Maintenance' | string;
  readonly isBookable: boolean;
  readonly images: readonly VehicleImage[];
  readonly createdAt: string;
}

/** What the add/edit form sends. Mirrors VehicleRequest on the API. */
export interface VehicleRequest {
  readonly carTypeId: string;
  readonly make: string;
  readonly model: string;
  readonly year: number;
  readonly color: string | null;
  readonly seats: number;
  readonly transmission: string;
  readonly fuelType: string;
  readonly description: string | null;
  readonly plateNumber: string;
  readonly dailyRate: number;
  readonly securityDeposit: number;
  readonly isDeliveryEligible: boolean;
  readonly mileageUnlimited: boolean;
  readonly mileageDailyLimitKm: number | null;
  readonly mileageExcessFeePerKm: number | null;
  readonly fuelPolicy: string;
}

/** Step one of the presigned upload: where to PUT the bytes, and the key they will live under. */
export interface UploadTicket {
  readonly uploadUrl: string;
  readonly storageKey: string;
  readonly expiresAt: string;
}

export type VehicleStatusAction =
  'Publish' | 'Hide' | 'SendToMaintenance' | 'ReturnFromMaintenance';
