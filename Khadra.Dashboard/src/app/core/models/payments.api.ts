import { AdminFinancialDocumentListItem } from './financial-documents.api';
import { FinancialPayment } from './financials.api';
import { Money } from './fleet.api';

/**
 * The administrator's view of money across the platform (payments Phase 4b): every checkout attempt,
 * one payment's page, and the refunds queue. Every figure is the server's; the screens word them.
 */

/** One checkout attempt in `GET /api/v1/admin/payments`. */
export interface AdminPaymentListItem {
  readonly paymentId: string;
  readonly bookingId: string;
  /** Null when the booking no longer resolves. */
  readonly bookingReference: string | null;
  readonly dealerId: string | null;
  /** Null when the dealership no longer resolves. */
  readonly dealerName: string | null;
  readonly customerId: string;
  /** Null when the customer's account no longer resolves. */
  readonly customerName: string | null;
  readonly purpose: string;
  readonly status: string;
  /** The payment's own verdict: `None`, `InProgress`, `Delayed`, `Partial` or `Complete`. */
  readonly refundProgress: string;
  readonly amountCharged: Money;
  readonly processingFee: Money;
  readonly appliedToBooking: Money;
  readonly refundSettled: Money;
  readonly isSandbox: boolean;
  readonly providerReference: string | null;
  readonly failureCode: string | null;
  readonly orphanReason: string | null;
  readonly createdAt: string;
  readonly occurredAt: string;
}

/** One refund in `GET /api/v1/admin/refunds`: what is owed back, to whom, and where it is. */
export interface AdminRefundListItem {
  readonly refundId: string;
  readonly paymentId: string;
  readonly bookingId: string;
  readonly bookingReference: string | null;
  readonly dealerId: string | null;
  readonly dealerName: string | null;
  readonly customerId: string;
  readonly customerName: string | null;
  readonly reason: string;
  readonly status: string;
  readonly amount: Money;
  readonly disputeTicketId: string | null;
  readonly requestedAt: string;
  readonly sentAt: string | null;
  readonly settledAt: string | null;
  readonly failedAt: string | null;
  readonly failureCode: string | null;
  readonly providerReference: string | null;
  readonly isSandbox: boolean;
  /** How many sends the provider refused (Wave 4, B4). Optional, as the API made it. */
  readonly refusalCount?: number;
  /** When a refused refund is sent again; null when nothing waits. */
  readonly nextAttemptAt?: string | null;
  /** Refused often enough that an administrator must look: the server's judgement, by its own setting. */
  readonly needsAPerson?: boolean;
}

/** The booking a payment belongs to, with its parties. */
export interface PaymentBookingLink {
  readonly bookingId: string;
  readonly reference: string;
  readonly status: string;
  readonly dealerId: string;
  readonly dealerName: string | null;
  readonly customerId: string;
  readonly customerName: string | null;
}

/** One event the provider sent about a payment. */
export interface ProviderEvent {
  readonly receiptId: string;
  readonly providerEventId: string;
  readonly kind: string;
  /**
   * `Acted`, `Orphaned`, `Unknown`, `Ignored` or `Unmatched`; and for a capture notice about money the payment
   * had already taken (Wave 4, B1), `Duplicate`, `AssumedDuplicate`, `AmountMismatch`, `SecondCapture` or
   * `OtherAttempt`.
   */
  readonly outcome: string;
  readonly amount: Money | null;
  readonly receivedAt: string;
  /** `Payment` when the receipt names the payment; `Reference` when it carries only its reference. */
  readonly tiedBy: 'Payment' | 'Reference' | string;
  /** The provider's id for the capture a capture notice reported, when it carried one (Wave 4, B1). */
  readonly captureReference?: string | null;
}

/**
 * A capture notice that money may have moved in a way no booking accounts for (Wave 4, B1). Never refunded by
 * the platform: somebody deals with it at the provider, then marks it handled here with a note.
 */
export interface PaymentIncident {
  readonly incidentId: string;
  /** `SecondCapture`, `AmountMismatch` or `CaptureOnAnotherAttempt`. */
  readonly kind: string;
  readonly receiptId: string;
  readonly captureReference: string | null;
  /** What the notice said was captured. */
  readonly reported: Money;
  /** What this payment had already taken — or, for a capture on another attempt, what it asked for. */
  readonly expected: Money;
  /** For a capture on another attempt: the attempt that holds the capture. */
  readonly otherPaymentId: string | null;
  readonly detectedAt: string;
  readonly handledAt: string | null;
  /** The administrator who marked it handled, by name. */
  readonly handledBy: string | null;
  readonly handledNote: string | null;
}

/** `GET /api/v1/admin/payments/{id}`: the same description a booking's financial state gives the payment. */
export interface AdminPayment {
  readonly payment: FinancialPayment;
  readonly booking: PaymentBookingLink | null;
  readonly providerEvents: readonly ProviderEvent[];
  /**
   * The payment's receipts, every version (payments Phase 5b). Optional, as the API made it: an API
   * without documents sends none, and the page says nothing about them.
   */
  readonly documents?: readonly AdminFinancialDocumentListItem[] | null;
  /** The payment's capture incidents, open ones first (Wave 4, B1). Optional, as the API made it. */
  readonly incidents?: readonly PaymentIncident[] | null;
}

/** The words the payments screens filter on, from the domain's own enumerations: the console keeps no list. */
export interface PaymentVocabulary {
  readonly paymentStatuses: readonly string[];
  readonly paymentPurposes: readonly string[];
  readonly refundStatuses: readonly string[];
  readonly refundReasons: readonly string[];
}
