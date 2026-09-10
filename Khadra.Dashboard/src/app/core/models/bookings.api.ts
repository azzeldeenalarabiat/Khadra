import { Money } from './fleet.api';

/**
 * Bookings as the API returns them (Khadra.Application/Bookings/Dtos and ReadModels).
 *
 * Every figure here is the FROZEN one from the booking — the price, deposit and terms it was made
 * under — never a current setting. A screen that recomputed any of it from today's rules would be
 * quietly wrong in exactly the way the snapshot exists to prevent.
 */

/** One row of a bookings list. */
export interface BookingListItem {
  readonly bookingId: string;
  readonly reference: string;
  readonly status: BookingStatus;
  readonly periodStart: string;
  readonly periodEnd: string;
  /**
   * The billed calendar days, frozen on the booking when it was made.
   *
   * Never recompute this from the two instants above. Subtracting them gives elapsed time, which is
   * the rule the platform stopped using on 2026-09-07, and the screen would contradict the invoice.
   */
  readonly days: number;
  readonly pickupMethod: 'SelfPickup' | 'Delivery';
  readonly totalPrice: number;
  readonly currency: string;
  readonly createdAt: string;
  /** Null when the car has since been removed from the platform: the booking outlives the listing. */
  readonly vehicle: VehicleLabel | null;
  readonly dealerName: string;
  readonly customerName: string;
  readonly hasLiveDispute: boolean;
  /**
   * Both parties by id, so a platform-wide row can open the dealership or the customer behind it.
   * A dealer or a customer reading their own list already knows one of them; the Admin knows neither.
   */
  readonly dealerId: string;
  readonly customerId: string;
}

export type BookingStatus =
  | 'Requested'
  | 'Approved'
  | 'Confirmed'
  | 'Rejected'
  | 'PickedUp'
  | 'Returned'
  | 'Completed'
  | 'Cancelled'
  | 'NoShow'
  | 'Expired';

export interface VehicleLabel {
  readonly vehicleId: string;
  readonly make: string;
  readonly model: string;
  readonly year: number;
  readonly color: string | null;
  readonly plateNumber: string;
  readonly coverImageUrl: string | null;
}

export interface Booking {
  readonly bookingId: string;
  readonly reference: string;
  readonly status: BookingStatus;
  readonly isTerminal: boolean;
  readonly customerId: string;
  readonly dealerId: string;
  readonly vehicleId: string;
  readonly periodStart: string;
  readonly periodEnd: string;
  readonly pickupMethod: 'SelfPickup' | 'Delivery';
  readonly deliveryLocation: { readonly latitude: number; readonly longitude: number } | null;
  readonly paymentOption: string;
  readonly pricing: BookingPricing;
  readonly terms: BookingTerms;
  /** Terms.CommissionPercent of Pricing.RentalTotal, computed server-side at the frozen rate. */
  readonly commissionAmount: Money;
  readonly penalty: PenaltyAssessment | null;
  readonly cancelledBy: string | null;
  readonly cancellationReason: string | null;
  readonly createdAt: string;
  /** When the dealer must answer by. */
  readonly decisionDeadline: string;
  /** Null until the dealer approves: there is no payment clock before there is a decision. */
  readonly paymentDeadline: string | null;
  /** Whether the deposit cleared. Never inferred from the status on a screen. */
  readonly depositPaid: boolean;
  readonly requestedAt: string | null;
  readonly approvedAt: string | null;
  readonly freeCancellationDeadline: string | null;
  readonly pickedUpAt: string | null;
  readonly returnedAt: string | null;
  readonly finishedAt: string | null;
  /** Judged server-side against this booking's own frozen window. */
  readonly canBeDisputed: boolean;
  readonly liveDisputeId: string | null;
  readonly vehicle: VehicleLabel | null;
  readonly dealerName: string;
  readonly customerName: string;
  readonly handovers: readonly Handover[];
  readonly history: readonly BookingStatusChange[];
}

export interface BookingPricing {
  readonly dailyRate: Money;
  readonly days: number;
  readonly rentalTotal: Money;
  readonly deliveryFee: Money;
  readonly totalPrice: Money;
  readonly depositPercent: number;
  readonly depositAmount: Money;
  readonly balanceDue: Money;
  readonly securityDeposit: Money;
  readonly mileageUnlimited: boolean;
  readonly mileageDailyLimitKm: number | null;
  readonly mileageExcessFeePerKm: Money | null;
  readonly fuelPolicy: string;
}

export interface BookingTerms {
  readonly depositPercent: number;
  readonly commissionPercent: number;
  readonly freeCancellationWindowHours: number;
  readonly noShowTimeoutHours: number;
  readonly paymentWindowHours: number;
  readonly postReturnSettlementWindowHours: number;
  readonly customerCancellationPenaltyPercent: number;
  readonly dealerPenaltyMinPercent: number;
  readonly dealerPenaltyMaxPercent: number;
  readonly rulesVersion: number;
}

/** What a penalty WOULD be. Assessed, never charged: only a resolved dispute moves money. */
export interface PenaltyAssessment {
  readonly attributedTo: 'Customer' | 'Dealer' | 'System' | 'Unattributed' | 'Admin';
  readonly minPercent: number;
  readonly maxPercent: number;
  readonly minAmount: Money;
  readonly maxAmount: Money;
  readonly isRange: boolean;
  readonly isNothingOwed: boolean;
  readonly reason: string;
  readonly assessedAt: string;
}

export interface Handover {
  readonly type: 'Pickup' | 'Return';
  readonly recordedBy: string;
  readonly recordedByUserId: string;
  readonly odometerKm: number | null;
  readonly fuelLevel: number | null;
  readonly notes: string | null;
  readonly cashCollected: Money | null;
  readonly photoCount: number;
  readonly recordedAt: string;
}

export interface BookingStatusChange {
  readonly fromStatus: string | null;
  readonly toStatus: string;
  readonly actorParty: string;
  readonly actorUserId: string | null;
  readonly reason: string | null;
  readonly occurredAt: string;
}

export interface PagedResult<T> {
  readonly items: readonly T[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totalPages: number;
}

/** The four documents spec 5.1 asks a renter for. The server's own enum names. */
export type RenterDocumentType =
  | 'DrivingLicenceFront'
  | 'DrivingLicenceBack'
  | 'NationalId'
  | 'Passport';

/**
 * One of the renter's documents on a booking this gallery is handling
 * (`GET /bookings/{id}/renter-documents`).
 *
 * There is NO url and NO storage key on this shape, and that is deliberate rather than an oversight
 * the console should work around. The bytes come from a sibling endpoint that re-checks the booking
 * relationship on every request, so the only thing the browser ever holds is a booking id and a
 * document id it was given. Never build an address from anything else here.
 */
export interface RenterDocument {
  readonly documentId: string;
  readonly type: RenterDocumentType;
  /** `PendingReview` for everything today: nothing on the platform reviews these yet. */
  readonly status: string;
  /** What the download will actually be served as. The console never guesses it from the type. */
  readonly contentType: string;
  readonly uploadedAt: string;
}

/** What the gallery may see about the renter's paperwork, while the booking is live. */
export interface RenterDocuments {
  readonly documents: readonly RenterDocument[];
  readonly isComplete: boolean;
  /** The types the renter has not filed. The SERVER decides this; the console never derives it. */
  readonly missing: readonly RenterDocumentType[];
}
