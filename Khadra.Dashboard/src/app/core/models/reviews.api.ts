/**
 * Review moderation (pre-launch item 81), as `GET /api/v1/admin/reviews` sends it.
 *
 * Both directions share one shape. For a customer's review of an office the reviewer is the customer and the subject
 * the office; for an office's rating of a customer the reviewer is the office and the subject the customer. Which id
 * links where follows from `direction`.
 */
export type ReviewDirection = 'CustomerRatesDealer' | 'DealerRatesCustomer';

export interface ModerationReview {
  readonly reviewId: string;
  readonly direction: ReviewDirection | string;
  readonly rating: number;
  /** The whole comment, hidden or not: judging it is the point. An office's rating of a customer has none. */
  readonly comment: string | null;
  readonly isHidden: boolean;
  /** A policy reason code (`REVIEW_HIDE_REASONS`), worded by the console. */
  readonly hiddenReason: string | null;
  readonly createdAt: string;
  readonly visibleFrom: string;
  /** Whether the blind window has passed: before it, nobody but the author can see the review. */
  readonly isPublished: boolean;
  readonly bookingId: string;
  readonly bookingReference: string | null;
  readonly reviewerId: string;
  /** Null when the name no longer resolves. */
  readonly reviewerName: string | null;
  readonly subjectId: string;
  readonly subjectName: string | null;
}

/** The policy reasons the server accepts (`ReviewHideReason`), in the order the dialog offers them. */
export const REVIEW_HIDE_REASONS = [
  'PersonalContactDetails',
  'AbusiveLanguage',
  'NotAboutThisRental',
  'SpamOrPromotion',
] as const;
