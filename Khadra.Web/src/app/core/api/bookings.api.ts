import { BookingPricing } from './catalogue.api';
import { Money } from './common.api';

/**
 * Bookings, as the customer sees their own: `GET /bookings`, `/bookings/tab-counts`,
 * `/bookings/next`, `/bookings/{id}`, and the writes beside them. Hand-written to `BookingDto`,
 * `BookingListItem` and `PaymentDtos`. Every flag here is the SERVER's judgement at the moment it
 * answered — what may be cancelled, paid, disputed, reviewed — and a page shows or hides an action
 * by it, never by comparing a deadline with the browser's clock.
 */

/** The platform's booking statuses. Words for them are the page's; the names are the API's. */
export type BookingStatus =
  | 'Requested'
  | 'Approved'
  | 'Confirmed'
  | 'PickedUp'
  | 'Returned'
  | 'Completed'
  | 'Cancelled'
  | 'Rejected'
  | 'NoShow'
  | 'Expired';

export const BOOKING_TABS = ['all', 'pending', 'upcoming', 'active', 'returned', 'completed', 'closed', 'disputed'] as const;
export type BookingTab = (typeof BOOKING_TABS)[number];

export interface VehicleLabel {
  readonly vehicleId: string;
  readonly make: string;
  readonly model: string;
  readonly year: number;
  readonly color: string | null;
  readonly plateNumber: string;
  readonly coverImageUrl: string | null;
}

export interface BookingListItem {
  readonly bookingId: string;
  readonly reference: string;
  readonly status: BookingStatus | string;
  readonly periodStart: string;
  readonly periodEnd: string;
  /** Billed calendar days, frozen on the booking. Never recomputed from the two instants. */
  readonly days: number;
  readonly pickupMethod: string;
  readonly totalPrice: number;
  readonly currency: string;
  readonly createdAt: string;
  readonly vehicle: VehicleLabel | null;
  readonly dealerName: string;
  readonly dealerRemoved: boolean;
  readonly hasLiveDispute: boolean;
  readonly dealerId?: string;
}

export interface BookingTerms {
  readonly depositPercent: number;
  readonly freeCancellationWindowHours: number;
  readonly noShowTimeoutHours: number;
  readonly paymentWindowHours: number;
  readonly answerWindowHours: number;
  readonly customerCancellationPenaltyPercent: number;
}

export interface PenaltyAssessment {
  readonly attributedTo: string;
  readonly minPercent: number;
  readonly maxPercent: number;
  readonly minAmount: Money;
  readonly maxAmount: Money;
  readonly isRange: boolean;
  readonly isNothingOwed: boolean;
  readonly requiresTicketToEnforce: boolean;
  readonly reason: string;
  readonly reasonCode: string | null;
  readonly assessedAt: string;
}

export interface PaymentAttempt {
  readonly paymentId: string;
  readonly bookingId: string;
  readonly status: 'Initiated' | 'Pending' | 'Failed' | 'Applied' | string;
  readonly amount: Money;
  readonly checkoutUrl: string | null;
  readonly expiresAt: string;
  readonly failureCode: string | null;
  readonly createdAt: string;
  readonly isSandbox: boolean;
}

/** Whether the deposit can be paid now, and what is in the way if not — the server's call. */
export interface PaymentAvailability {
  readonly canPay: boolean;
  /** `payments.provider_unavailable` when the platform takes no cards at all. */
  readonly unavailableReason: string | null;
  readonly amountDue: Money | null;
  readonly payBy: string | null;
  readonly liveAttempt: PaymentAttempt | null;
}

export interface Handover {
  readonly type: 'Pickup' | 'Return' | string;
  readonly recordedBy: string;
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
  readonly reasonCode: string | null;
  readonly reason: string | null;
  readonly occurredAt: string;
}

export interface Booking {
  readonly bookingId: string;
  readonly reference: string;
  readonly status: BookingStatus | string;
  readonly isTerminal: boolean;
  readonly dealerId: string;
  readonly vehicleId: string;
  readonly periodStart: string;
  readonly periodEnd: string;
  readonly pickupMethod: 'SelfPickup' | 'Delivery' | string;
  readonly deliveryLocation: { readonly latitude: number; readonly longitude: number } | null;
  readonly pricing: BookingPricing;
  readonly terms: BookingTerms;
  readonly penalty: PenaltyAssessment | null;
  readonly cancelledBy: string | null;
  readonly cancellationReasonCode: string | null;
  readonly cancellationReason: string | null;
  readonly createdAt: string;
  readonly decisionDeadline: string;
  readonly paymentDeadline: string | null;
  readonly depositPaid: boolean;
  readonly requestedAt: string | null;
  readonly approvedAt: string | null;
  readonly freeCancellationDeadline: string | null;
  readonly pickedUpAt: string | null;
  readonly returnedAt: string | null;
  readonly finishedAt: string | null;
  readonly canBeDisputed: boolean;
  readonly isAwaitingDecision: boolean;
  readonly isAwaitingPayment: boolean;
  readonly cancellation: { readonly canCancel: boolean; readonly isFree: boolean; readonly penalty: PenaltyAssessment };
  readonly liveDisputeId: string | null;
  readonly payment: PaymentAvailability | null;
  readonly canReportNonDelivery: boolean;
  readonly nonDeliveryReportableFrom: string;
  readonly canBeReviewed: boolean;
  readonly myReviewId: string | null;
  readonly vehicle: VehicleLabel | null;
  readonly dealerName: string;
  readonly dealerRemoved: boolean;
  readonly dealerCityId: string | null;
  readonly handovers: readonly Handover[];
  readonly history: readonly BookingStatusChange[];
}

/** `POST /bookings/{id}/handover-code`. */
export interface HandoverCode {
  readonly type: 'Pickup' | 'Return' | string;
  readonly code: string;
  readonly qrPayload: string;
  readonly expiresAt: string;
}

/** `GET /bookings/next`. */
export interface NextBooking {
  readonly booking: BookingListItem;
  readonly reason: 'AwaitingPayment' | 'InProgress' | 'Upcoming' | 'AwaitingDecision' | string;
}
