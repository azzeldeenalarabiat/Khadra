import { Money } from './common.api';

/**
 * `GET /api/v1/bookings/{id}/financials` — a booking's financial state as the CUSTOMER may see it
 * (payments Phase 4). Every figure is the server's; the page renders them and computes none. Fields the
 * customer is never sent (commission, provider references, failure codes) are left out here.
 */
export interface BookingFinancials {
  readonly bookingId: string;
  readonly bookingStatus: string;
  readonly currency: string;
  readonly generatedAt: string;
  readonly calculatorVersion: number;
  /** The records contradict one another: the figures are what they say, and Khadra will look. */
  readonly needsReview: boolean;
  readonly summary: FinancialSummary;
  readonly balance: FinancialBalance;
  readonly deposit: FinancialDeposit;
  readonly payments: readonly FinancialPayment[];
}

export interface FinancialSummary {
  readonly rentalSubtotal: Money;
  readonly deliveryFee: Money;
  readonly bookingTotal: Money;
  readonly requiredDeposit: Money;
  readonly securityDeposit: Money;
  readonly paidOnline: Money;
  readonly processingFees: Money | null;
  readonly chargedOnline: Money | null;
  readonly refunded: Money;
  readonly refundInProgress: Money;
  readonly refundDelayed: Money;
}

/** `NotYetDue`, `DueAtHandover`, `CashAtHandover`, `PaidInFull` or `NotDue`. */
export interface FinancialBalance {
  readonly state: string;
  readonly amount: Money;
  readonly cashRecorded: readonly { readonly handover: 'Pickup' | 'Return' | string; readonly amount: Money; readonly recordedAt: string }[];
}

/**
 * `NotPaid`, `Held`, `AppliedToRental`, `InSettlementWindow`, `UnderDispute`, `SettledWithRental`,
 * `ReturnedWithPayment`, `HeldUntilWindowCloses`, `HeldForAssessedPenalty`, `HeldUnresolved`, `KeptAsPenalty` (payments Phase 8),
 * `Released` or `DecidedByDispute`.
 */
export interface FinancialDeposit {
  readonly state: string;
  readonly amount: Money;
  readonly windowEndsAt: string | null;
  readonly refund: FinancialRefund | null;
  readonly decision: {
    readonly ticketIds: readonly string[];
    readonly decidedAt: string;
    /** The customer's own share: the only one the customer is sent (owner, 2026-09-26). */
    readonly toCustomer: Money | null;
    readonly toCustomerRefundStatus: string | null;
  } | null;
}

export interface FinancialPayment {
  readonly paymentId: string;
  readonly purpose: 'Deposit' | 'FullPayment' | string;
  /** `Applied`, or `Orphaned` for a capture that could not be applied (it is refunded in full). */
  readonly status: string;
  /** `None`, `InProgress`, `Delayed`, `Partial` or `Complete`. */
  readonly refundProgress: string;
  readonly occurredAt: string;
  readonly appliedToBooking: Money;
  readonly amountCharged: Money | null;
  readonly processingFee: Money | null;
  readonly feeRefundable: boolean | null;
  readonly refunds: readonly FinancialRefund[];
}

export interface FinancialRefund {
  readonly refundId: string;
  readonly paymentId: string;
  readonly reason: string;
  readonly status: 'Requested' | 'Sent' | 'Settled' | 'Failed' | string;
  readonly amount: Money;
  readonly feePart: Money | null;
  readonly requestedAt: string;
  readonly sentAt: string | null;
  readonly settledAt: string | null;
  readonly failedAt: string | null;
  readonly disputeTicketId: string | null;
}
