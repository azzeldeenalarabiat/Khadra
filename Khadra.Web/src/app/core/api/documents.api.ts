/** `GET /api/v1/customers/me/documents` — the customer's own identity documents. */
export interface CustomerDocuments {
  readonly documents: readonly CustomerDocument[];
  /** The server's verdict; a page never works this out from the list. */
  readonly isComplete: boolean;
  /** Document types still needed, by API name. */
  readonly missing: readonly string[];
}

export interface CustomerDocument {
  readonly documentId: string;
  readonly type: 'DrivingLicenceFront' | 'DrivingLicenceBack' | 'NationalId' | 'Passport' | string;
  readonly status: 'PendingReview' | 'Verified' | 'Rejected' | string;
  readonly contentType: string;
  readonly sizeBytes: number;
  readonly uploadedAt: string;
  /** Why it was rejected, as the reviewer wrote it. */
  readonly reviewNote: string | null;
}
