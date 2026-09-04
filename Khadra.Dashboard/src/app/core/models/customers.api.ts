/**
 * Customers as the API returns them (Khadra.Application/IdentityAccess/ReadModels).
 *
 * Documents are DESCRIBED, never linked. Spec 7 keeps identity papers private and the domain scopes
 * viewing to the customer themselves and to a dealer with an active request; an administrator is not
 * named there, and the review mechanism spec 5.1 anticipates has not been decided. So the profile
 * says what is on file and what state it is in, and no signed URL for a passport is ever minted.
 */

export type AccountStatus = 'Active' | 'Suspended';

export interface CustomerListItem {
  readonly userId: string;
  readonly fullName: string;
  readonly email: string;
  readonly phone: string;
  readonly status: AccountStatus;
  readonly isEmailVerified: boolean;
  readonly lastLoginAt: string | null;
  readonly createdAt: string;
  readonly bookingCount: number;
  readonly documentCount: number;
  readonly hasCompleteRenterDocuments: boolean;
}

export interface CustomerProfile {
  readonly userId: string;
  readonly fullName: string;
  readonly email: string;
  readonly phone: string;
  readonly status: AccountStatus;
  readonly suspensionReason: string | null;
  readonly isEmailVerified: boolean;
  readonly emailVerifiedAt: string | null;
  readonly dateOfBirth: string | null;
  readonly isForeignNational: boolean;
  readonly createdAt: string;
  readonly lastLoginAt: string | null;
  readonly passwordChangedAt: string | null;
  readonly hasCompleteRenterDocuments: boolean;
  readonly documents: readonly CustomerDocumentSummary[];
  readonly bookings: CustomerBookingTotals;
}

export interface CustomerDocumentSummary {
  readonly documentId: string;
  readonly type: string;
  readonly status: 'PendingReview' | 'Verified' | 'Rejected';
  readonly contentType: string;
  readonly sizeBytes: number;
  readonly uploadedAt: string;
  readonly reviewNote: string | null;
}

export interface CustomerBookingTotals {
  readonly total: number;
  readonly completed: number;
  readonly cancelled: number;
  readonly noShow: number;
  readonly live: number;
}

export interface CustomerCounts {
  readonly total: number;
  readonly active: number;
  readonly suspended: number;
  readonly unverified: number;
}
