/**
 * The wire shape of the Admin dealer endpoints.
 *
 * A faithful mirror of the API and nothing more. `canTrade` is sent by the server rather than
 * inferred here, because it is three conditions (approved AND not suspended AND not deleted) and
 * recomputing it in the browser is how one of them eventually gets forgotten.
 */

export interface PagedResult<T> {
  readonly items: readonly T[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totalPages: number;
  readonly hasPrevious: boolean;
  readonly hasNext: boolean;
}

export interface DealerListItem {
  readonly dealerId: string;
  readonly businessName: string;
  readonly commercialRegistrationNumber: string;
  readonly verificationStatus: string;
  readonly isSuspended: boolean;
  readonly canTrade: boolean;
  readonly submittedAt: string;
  readonly reviewDueAt: string;
  readonly createdAt: string;
  readonly documentCount: number;
  readonly employeeCount: number;
  readonly carCount: number;
  /** Null means no reviews yet — render that, never a zero rating. */
  readonly averageRating: number | null;
  readonly reviewCount: number;
}

export interface DealerProfile {
  readonly dealerId: string;
  readonly businessName: string;
  readonly commercialRegistrationNumber: string;
  readonly verificationStatus: string;
  readonly reviewNote: string | null;
  readonly submittedAt: string;
  readonly reviewDueAt: string;
  readonly canTrade: boolean;
  readonly isSuspended: boolean;
  readonly submittedDocuments: readonly string[];
  readonly missingDocuments: readonly string[];
}

/** Spec 7: a short-lived signed link, minted per request and never stored. */
export interface DealerDocumentLink {
  readonly type: string;
  readonly url: string;
  readonly expiresAt: string;
}

export interface DealerReviewTimelineEntry {
  readonly label: string;
  readonly detail: string;
  readonly occurredAt: string;
  readonly isComplete: boolean;
}

export interface DealerReview {
  readonly dealer: DealerProfile;
  readonly description: string | null;
  readonly latitude: number;
  readonly longitude: number;
  readonly documents: readonly DealerDocumentLink[];
  readonly timeline: readonly DealerReviewTimelineEntry[];
  readonly isBreachingSla: boolean;
  readonly employeeCount: number;
}
