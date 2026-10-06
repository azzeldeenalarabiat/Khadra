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
  /**
   * Where the penalty stands, as the server reads its own dispute records (pre-launch item 173):
   * "Assessed" (nothing charged yet), "ResolvedByDispute", or "KeptFromDeposit" (the window closed with no dispute and
   * the penalty was kept from the deposit, payments Phase 8). Absent on a cancellation preview.
   */
  readonly state?: string | null;
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
  /** "Deposit" or "FullPayment". Absent from an API older than 2026-09-24. */
  readonly purpose?: PaymentPurpose;
  readonly processingFee?: Money;
}

/** The two ways an approved booking can be paid (2026-09-24). */
export type PaymentPurpose = 'Deposit' | 'FullPayment';

/** One way of paying, every figure the server's: the page shows these and computes none. */
export interface PaymentOption {
  readonly purpose: PaymentPurpose;
  readonly selectedPaymentAmount: Money;
  readonly processingFee: Money;
  readonly totalChargedNow: Money;
  readonly remainingBalanceAfter: Money;
}

/** Whether the deposit can be paid now, and what is in the way if not — the server's call. */
export interface PaymentAvailability {
  readonly canPay: boolean;
  /** `payments.provider_unavailable` when the platform takes no cards at all. */
  readonly unavailableReason: string | null;
  readonly amountDue: Money | null;
  readonly payBy: string | null;
  readonly liveAttempt: PaymentAttempt | null;
  /** Deposit first, then the full amount. Empty when the booking cannot be paid now. */
  readonly options?: readonly PaymentOption[];
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
  /** How the handover was proved: "Code", "Unverified" or "NotRequired"; absent on older records. */
  readonly verification?: string | null;
  /** The office's reason, when it recorded the handover without the customer's code. */
  readonly unverifiedReason?: string | null;
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
  readonly cancellation: {
    readonly canCancel: boolean;
    readonly isFree: boolean;
    readonly penalty: PenaltyAssessment;
    /** Cancelling now returns the PAID deposit in full to the original payment method. Absent on an older API. */
    readonly willRefundDeposit?: boolean;
    /**
     * What cancelling now returns to the card, all of it (Phase 3): the whole payment inside the free
     * window, everything above the deposit after it; null when nothing. Sent back as `expectedRefund`.
     * Absent on an older API.
     */
    readonly refundAmount?: Money | null;
  };
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
  /** The deposit a free cancellation returned, with the refund's own status. Absent on an older API. */
  readonly depositRefund?: DepositRefund | null;
  /** What was paid online towards the booking, fees excluded. Absent on an older API. */
  readonly onlinePaid?: Money | null;
  /**
   * The whole total has been paid online: the server's verdict every "paid in full" sentence keys on
   * (owner, 2026-09-25). Absent on an older API, which leaves the deposit wording in place.
   */
  readonly isPaidInFull?: boolean;
  /** The payment that confirmed the booking. Absent on an older API; null while none has. */
  readonly confirmingPayment?: ConfirmingPayment | null;
  /** Every refund against this booking's payments, oldest first (Phase 3). Absent on an older API. */
  readonly refunds?: readonly Refund[];
  /** What has reached the customer: the settled refunds, in the booking's currency. Absent on an older API. */
  readonly refundedAmount?: Money | null;
  /** What is promised back and not there yet. Absent on an older API. */
  readonly refundOutstandingAmount?: Money | null;
  /**
   * The earliest moment the rental office may record the pickup (Wave 3 D4, owner 2026-10-05): the rental start less
   * the turnaround frozen on the booking. The pickup code is offered from then. Absent on an older API, which had no
   * window.
   */
  readonly pickupAvailableFrom?: string | null;
  /** The earliest moment the return may be recorded: the rental's start. The return code is offered from then. */
  readonly returnAvailableFrom?: string | null;
}

/**
 * One refund, as the server records it (Phase 3). `reason` is a code this site words; one it does not
 * know yet reads as a plain "Refund".
 */
export interface Refund {
  readonly refundId: string;
  readonly paymentId: string;
  readonly reason:
    | 'FreeCancellation'
    | 'PlatformCancellation'
    | 'EndedBeforePickup'
    | 'DisputeWindowClosed'
    | 'DisputeResolution'
    | 'OrphanedCapture'
    | string;
  readonly amount: Money;
  readonly status: 'Requested' | 'Sent' | 'Settled' | 'Failed' | string;
  readonly requestedAt: string;
  readonly sentAt: string | null;
  readonly settledAt: string | null;
  readonly failedAt: string | null;
  readonly disputeTicketId: string | null;
}

/**
 * The payment that confirmed a booking: what kind it was and what it charged, all the server's own
 * figures. `purpose` is 'Deposit' or 'FullPayment', the only two that ever confirm a booking.
 */
export interface ConfirmingPayment {
  readonly purpose: PaymentPurpose | string;
  /** What the card was charged, the processing fee included. */
  readonly amountCharged: Money;
  readonly processingFee: Money;
  readonly appliedToBooking: Money;
  readonly paidAt: string | null;
  /** What a free cancellation would return, from the same rule the refund itself applies. */
  readonly refundOnFreeCancellation: Money;
  /** The fee that goes back with the booking money. Absent on an older API. */
  readonly refundableFee?: Money | null;
}

/**
 * Where the deposit's refund is. `Requested` and `Sent` both mean "refund initiated" to a customer;
 * `Settled` is refunded; `Failed` is still owed and being retried by the server.
 */
export interface DepositRefund {
  readonly status: 'Requested' | 'Sent' | 'Settled' | 'Failed' | string;
  readonly amount: Money;
  readonly requestedAt: string;
  readonly sentAt: string | null;
  readonly settledAt: string | null;
  readonly failedAt: string | null;
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
