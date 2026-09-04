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
  /** What documentCount is out of. From the server, so the column cannot quote a stale total. */
  readonly requiredDocumentCount: number;
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
  readonly suspensionReason: string | null;
  readonly submittedAt: string;
  readonly reviewDueAt: string;
  readonly createdAt: string;
  readonly canTrade: boolean;
  readonly isSuspended: boolean;
  readonly submittedDocuments: readonly string[];
  readonly missingDocuments: readonly string[];
  /** Every type an approval requires (spec 3.1). The console counts these, it never assumes three. */
  readonly requiredDocuments: readonly string[];
  /** Spec 4.1: the dealer page. Editable by the owner. */
  readonly description: string | null;
  readonly latitude: number;
  readonly longitude: number;
  readonly operatingHours: readonly DaySchedule[];
  readonly delivery: DeliverySettings;
  /** Public, cacheable paths; null until the owner uploads one. */
  readonly logoUrl: string | null;
  readonly coverUrl: string | null;
  readonly employeeCount: number;
  /** Hints for which controls to show; every endpoint enforces the real answer. */
  readonly isOwner: boolean;
  readonly canViewReports: boolean;
}

export interface DaySchedule {
  /** 'Sunday' … 'Saturday'. */
  readonly day: string;
  readonly isClosed: boolean;
  readonly opensAt: string | null;
  readonly closesAt: string | null;
}

export interface DeliverySettings {
  readonly isEnabled: boolean;
  readonly radiusKm: number;
}

/** Spec 7: a short-lived signed link, minted per request and never stored. */
export interface DealerDocumentLink {
  readonly type: string;
  /** What the download will actually be served as. The console never guesses this from the type. */
  readonly contentType: string;
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
}
