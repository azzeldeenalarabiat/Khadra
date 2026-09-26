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
  /** `Acted`, `Orphaned`, `Unknown`, `Ignored` or `Unmatched`. */
  readonly outcome: string;
  readonly amount: Money | null;
  readonly receivedAt: string;
  /** `Payment` when the receipt names the payment; `Reference` when it carries only its reference. */
  readonly tiedBy: 'Payment' | 'Reference' | string;
}

/** `GET /api/v1/admin/payments/{id}`: the same description a booking's financial state gives the payment. */
export interface AdminPayment {
  readonly payment: FinancialPayment;
  readonly booking: PaymentBookingLink | null;
  readonly providerEvents: readonly ProviderEvent[];
}

/** The words the payments screens filter on, from the domain's own enumerations: the console keeps no list. */
export interface PaymentVocabulary {
  readonly paymentStatuses: readonly string[];
  readonly paymentPurposes: readonly string[];
  readonly refundStatuses: readonly string[];
  readonly refundReasons: readonly string[];
}
