import { Money } from './fleet.api';

/**
 * A booking's financial state (payments Phase 4), as the rental office reads it from
 * `GET /api/v1/bookings/{id}/financials` and the administrator from
 * `GET /api/v1/admin/bookings/{id}/financials`. Every figure is the server's; a screen renders them and
 * computes none. A field a reader may not see arrives as null for that reader: the office never gets a
 * processing fee, the customer's or the platform's dispute share, provider references or `issues`.
 */
export interface BookingFinancials {
  readonly bookingId: string;
  readonly bookingStatus: string;
  readonly currency: string;
  readonly generatedAt: string;
  readonly calculatorVersion: number;
  /** The records contradict one another: the figures are what they say, and somebody has to look. */
  readonly needsReview: boolean;
  readonly summary: FinancialSummary;
  readonly balance: FinancialBalance;
  readonly deposit: FinancialDeposit;
  /** Khadra's commission, frozen on the booking, with its state. Never sent to a customer. */
  readonly commission: FinancialCommission | null;
  readonly payments: readonly FinancialPayment[];
  /** What the records contradict, as stable codes. Administrator only. */
  readonly issues: readonly string[] | null;
}

export interface FinancialSummary {
  readonly rentalSubtotal: Money;
  readonly deliveryFee: Money;
  readonly bookingTotal: Money;
  readonly requiredDeposit: Money;
  readonly securityDeposit: Money;
  readonly paidOnline: Money;
  /** Null for the office. */
  readonly processingFees: Money | null;
  /** Null for the office. */
  readonly chargedOnline: Money | null;
  /** For the office: booking money only, without a dispute decision's share. */
  readonly refunded: Money;
  readonly refundInProgress: Money;
  readonly refundDelayed: Money;
  /** The billed calendar days, frozen on the booking: never counted by a screen. */
  readonly days: number;
  readonly dailyRate: Money;
  readonly depositPercent: number;
}

/** `NotYetDue`, `DueAtHandover`, `CashAtHandover`, `PaidInFull` or `NotDue`. */
export interface FinancialBalance {
  readonly state: string;
  readonly amount: Money;
  /** Cash the office recorded on a handover, as it recorded it: shown beside the balance, never compared with it. */
  readonly cashRecorded: readonly { readonly handover: 'Pickup' | 'Return' | string; readonly amount: Money; readonly recordedAt: string }[];
}

/**
 * `NotPaid`, `Held`, `AppliedToRental`, `InSettlementWindow`, `UnderDispute`, `SettledWithRental`,
 * `ReturnedWithPayment`, `HeldUntilWindowCloses`, `HeldForAssessedPenalty`, `HeldUnresolved`,
 * `Released` or `DecidedByDispute`.
 */
export interface FinancialDeposit {
  readonly state: string;
  readonly amount: Money;
  readonly windowEndsAt: string | null;
  readonly refund: FinancialRefund | null;
  readonly decision: FinancialDisputeDecision | null;
}

/** What resolved disputes decided, each reader seeing only its own share (owner, 2026-09-26). */
export interface FinancialDisputeDecision {
  readonly ticketIds: readonly string[];
  readonly decidedAt: string;
  /** The customer's share. Customer and administrator only. */
  readonly toCustomer: Money | null;
  readonly toCustomerRefundStatus: string | null;
  /** The office's share. Office and administrator only. */
  readonly toOffice: Money | null;
  /** A charge assessed to the office. Office and administrator only. */
  readonly chargedToOffice: Money | null;
  /** What the platform kept. Administrator only. */
  readonly keptByPlatform: Money | null;
}

/** `Projected`, `Expected`, `Earned`, `NotEarned`, `Undecided` or `NotApplicable`. */
export interface FinancialCommission {
  readonly amount: Money;
  readonly percent: number;
  readonly basis: string;
  readonly state: string;
}

export interface FinancialPayment {
  readonly paymentId: string;
  readonly purpose: 'Deposit' | 'FullPayment' | string;
  /** The office is shown only `Applied`; the administrator every attempt. */
  readonly status: string;
  /** `None`, `InProgress`, `Delayed`, `Partial` or `Complete`. */
  readonly refundProgress: string;
  readonly occurredAt: string;
  readonly appliedToBooking: Money;
  /** Null for the office. */
  readonly amountCharged: Money | null;
  /** Null for the office. */
  readonly processingFee: Money | null;
  readonly feeRefundable: boolean | null;
  readonly refunds: readonly FinancialRefund[];
  /** Administrator only. */
  readonly createdAt: string | null;
  readonly isSandbox: boolean | null;
  readonly providerReference: string | null;
  readonly failureCode: string | null;
  readonly orphanReason: string | null;
}

export interface FinancialRefund {
  readonly refundId: string;
  readonly paymentId: string;
  readonly reason: string;
  readonly status: 'Requested' | 'Sent' | 'Settled' | 'Failed' | string;
  /** For the office: booking money only. */
  readonly amount: Money;
  /** Null for the office. */
  readonly feePart: Money | null;
  readonly bookingPart: Money;
  readonly requestedAt: string;
  readonly sentAt: string | null;
  readonly settledAt: string | null;
  readonly failedAt: string | null;
  readonly disputeTicketId: string | null;
  /** Administrator only. */
  readonly providerReference: string | null;
  readonly failureCode: string | null;
}
