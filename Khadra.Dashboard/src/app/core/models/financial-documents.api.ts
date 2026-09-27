import { Money } from './fleet.api';

/**
 * Issued financial documents as the administrator reads them (payments Phase 5): every receipt and
 * statement, one document with its proof and its void, the holds on documents owed and not issued, and a
 * booking's documents. `/api/v1/admin/financial-documents` and `/api/v1/admin/bookings/{id}/financial-documents`.
 *
 * A document's own words and figures live in its stored `snapshot` and are read by the version 1 reader
 * (`features/payments/financial-document-content.ts`); what surrounds it — its standing, its links, its
 * void — is a live fact the server works out when it is read.
 */

/** A text in English and Arabic, as a document stored it. */
export interface Bilingual {
  readonly en: string;
  readonly ar: string;
}

/** The one figure a list shows for a document, with the label it was stored under. */
export interface FinancialDocumentHeadline {
  readonly label: Bilingual;
  /** A live figure for a list row: a plain number, formatted like every other amount on screen. */
  readonly amount: Money;
}

/** One document in the administrator's lists. */
export interface AdminFinancialDocumentListItem {
  readonly documentId: string;
  /** `PaymentReceipt`, `RefundReceipt` or `BookingStatement`. */
  readonly type: string;
  readonly number: string;
  readonly version: number;
  /** `Current`, `Superseded` or `Voided`, worked out when read. */
  readonly status: string;
  readonly bookingId: string;
  readonly bookingReference: string;
  readonly customerId: string;
  readonly dealerId: string;
  readonly title: Bilingual;
  readonly headline: FinancialDocumentHeadline;
  /** What issued it: `PaymentCaptured`, `RefundSettled`, `DisputeResolved`, `BookingEnded`, `CashRecorded` or `Correction`. */
  readonly cause: string;
  /** When the money event it records happened. */
  readonly occurredAt: string;
  readonly issuedAt: string;
  /** The server's reading of the provider the document froze; never parsed from the number. */
  readonly isTest: boolean;
}

/** Another document one points at. */
export interface FinancialDocumentLink {
  readonly documentId: string;
  readonly type: string;
  readonly number: string;
  readonly version: number;
  readonly status: string;
}

export interface FinancialDocumentLinks {
  /** Every version of the family, oldest first, this one included. */
  readonly versions: readonly FinancialDocumentLink[];
  readonly previousVersion: FinancialDocumentLink | null;
  /** Only the NEXT member, which can be a voided one: never the way to the newest. */
  readonly nextVersion: FinancialDocumentLink | null;
  /** For a voided document, the correction issued in its place. */
  readonly replacedBy: FinancialDocumentLink | null;
  /** For a refund receipt, the payment receipt it belongs to. */
  readonly paymentReceipt: FinancialDocumentLink | null;
  /** For a payment receipt, the current receipts of the refunds made from it. */
  readonly refundReceipts: readonly FinancialDocumentLink[];
}

/** One document as the customer reads it: the stored snapshot exactly as issued, its standing and its links. */
export interface FinancialDocument {
  readonly documentId: string;
  readonly type: string;
  readonly number: string;
  readonly version: number;
  readonly status: string;
  readonly bookingId: string;
  readonly bookingReference: string;
  readonly title: Bilingual;
  readonly headline: FinancialDocumentHeadline;
  readonly cause: string;
  readonly occurredAt: string;
  readonly issuedAt: string;
  /** The grammar the snapshot is written in. A reader gates on THIS, never on the snapshot's own field. */
  readonly snapshotSchemaVersion: number;
  /** The document itself, read only through `readDocumentContent`. */
  readonly snapshot: unknown;
  readonly links: FinancialDocumentLinks;
  readonly voided: { readonly voidedAt: string; readonly replacedBy: FinancialDocumentLink | null } | null;
}

/** An administrator's void, reason and all. */
export interface FinancialDocumentVoid {
  readonly voidedAt: string;
  readonly voidedByAdminId: string;
  /** Null when the administrator's account no longer resolves. */
  readonly voidedByName: string | null;
  /** The administrator's own words; customers never see them. */
  readonly reason: string;
  readonly replacedBy: FinancialDocumentLink | null;
}

/** `GET /admin/financial-documents/{id}`: the customer's page, plus its proof and its void. */
export interface AdminFinancialDocument {
  readonly document: FinancialDocument;
  readonly customerId: string;
  readonly dealerId: string;
  /** The payment provider the document froze. */
  readonly provider: string;
  readonly isTest: boolean;
  /** The SHA-256 of what was issued, the proof a document was not altered. */
  readonly contentSha256: string;
  /** For a statement: the money movements it covers, up to this instant. */
  readonly coversThrough: string | null;
  readonly checkpointFingerprint: string | null;
  readonly void: FinancialDocumentVoid | null;
}

/** A document family owed and not issued, and why. */
export interface FinancialDocumentHold {
  readonly holdId: string;
  readonly documentType: string;
  readonly subjectId: string;
  readonly bookingId: string;
  readonly bookingReference: string | null;
  /** `RecordsNeedReview`, `IssuerNotConfigured` or `SnapshotFailed`. */
  readonly reason: string;
  readonly attempts: number;
  readonly firstFailedAt: string;
  readonly lastFailedAt: string;
  readonly nextAttemptAt: string;
  /** The last failure as the server logged it: machine English, shown as it is. */
  readonly lastError: string | null;
}

/** A document owed and not issued yet: "being prepared". */
export interface PendingFinancialDocument {
  readonly type: string;
  readonly subjectId: string;
  readonly occurredAt: string;
}

/** A booking's documents, what is being prepared, and what is on hold — for its Money section. */
export interface AdminBookingFinancialDocuments {
  readonly bookingId: string;
  readonly documents: readonly AdminFinancialDocumentListItem[];
  readonly beingPrepared: readonly PendingFinancialDocument[];
  readonly holds: readonly FinancialDocumentHold[];
}

/** The words the documents screens filter on, from the domain's own enumerations. */
export interface FinancialDocumentVocabulary {
  readonly types: readonly string[];
  readonly statuses: readonly string[];
  readonly causes: readonly string[];
  readonly holdReasons: readonly string[];
}

/** What a void did: the voided document, and the correction that replaced it. */
export interface VoidedFinancialDocument {
  readonly voidedDocumentId: string;
  readonly voidedNumber: string;
  readonly replacementDocumentId: string;
  readonly replacementNumber: string;
}
