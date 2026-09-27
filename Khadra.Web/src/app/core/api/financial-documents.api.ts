import { Money } from './common.api';

/**
 * Issued financial documents (payments Phase 5): payment receipts, refund receipts and booking
 * statements, as `GET /customers/me/financial-documents`, `GET /financial-documents/{id}` and
 * `GET /bookings/{id}/financial-documents` send them. None of them is a tax invoice.
 *
 * Named "financial documents" throughout: on this website "documents" are the customer's identity
 * papers (`documents.api.ts`, `/profile/documents`).
 */

/** A text a document stored in both languages. */
export interface Bilingual {
  readonly en: string;
  readonly ar: string;
}

/** The three kinds of document, as the server names them. A list can still carry one this site has never seen. */
export const FINANCIAL_DOCUMENT_TYPES = ['PaymentReceipt', 'RefundReceipt', 'BookingStatement'] as const;
export type FinancialDocumentType = (typeof FINANCIAL_DOCUMENT_TYPES)[number];

/** The one figure a list shows, with the label the document stored for it. */
export interface FinancialDocumentHeadline {
  readonly label: Bilingual;
  readonly amount: Money;
}

/** One document in a list: the Invoices & Receipts page, or a booking's documents. */
export interface FinancialDocumentRow {
  readonly documentId: string;
  /** `PaymentReceipt`, `RefundReceipt`, `BookingStatement` — or a type this site does not know yet. */
  readonly type: string;
  readonly number: string;
  readonly version: number;
  /** `Current`, `Superseded`, `Voided` — or a standing this site does not know yet. */
  readonly status: string;
  readonly bookingId: string;
  readonly bookingReference: string;
  readonly title: Bilingual;
  readonly headline: FinancialDocumentHeadline;
  readonly cause: string;
  /** When the money it records moved. */
  readonly occurredAt: string;
  /** When it was issued: a document issued late for older money shows both. */
  readonly issuedAt: string;
}

/** Another document a document points at. */
export interface FinancialDocumentLink {
  readonly documentId: string;
  readonly type: string;
  readonly number: string;
  readonly version: number;
  readonly status: string;
}

export interface FinancialDocumentLinks {
  /** Every version of the document's family, oldest first, this one included. */
  readonly versions: readonly FinancialDocumentLink[];
  readonly previousVersion: FinancialDocumentLink | null;
  /** The NEXT member only — it can be a voided one. The newest is the last of `versions`. */
  readonly nextVersion: FinancialDocumentLink | null;
  readonly replacedBy: FinancialDocumentLink | null;
  /** On a refund receipt: the payment receipt it was issued against. */
  readonly paymentReceipt: FinancialDocumentLink | null;
  /** On a payment receipt: the current receipt of each refund made from its payment. */
  readonly refundReceipts: readonly FinancialDocumentLink[];
}

/** That a document was voided, and what replaced it. The reason is the administrator's alone. */
export interface FinancialDocumentVoidNotice {
  readonly voidedAt: string;
  readonly replacedBy: FinancialDocumentLink | null;
}

/** One document's page: its row, its stored snapshot exactly as issued, and its links. */
export interface FinancialDocumentPage extends FinancialDocumentRow {
  readonly snapshotSchemaVersion: number;
  /** The document itself. Read only through `readDocumentContent`, never trusted blindly. */
  readonly snapshot: unknown;
  readonly links: FinancialDocumentLinks;
  readonly voided: FinancialDocumentVoidNotice | null;
}

/** A document owed and not issued yet: "being prepared". */
export interface PendingFinancialDocument {
  readonly type: string;
  /** The payment, refund or booking it will be about. */
  readonly subjectId: string;
  /** When the money it will record moved. */
  readonly occurredAt: string;
}

/** A booking's documents, and what is still being prepared, so the page guesses nothing. */
export interface BookingFinancialDocuments {
  readonly bookingId: string;
  readonly documents: readonly FinancialDocumentRow[];
  readonly beingPrepared: readonly PendingFinancialDocument[];
}
