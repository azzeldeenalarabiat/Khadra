import { Booking } from './bookings.api';
import { Money } from './fleet.api';

/** A dispute as the API returns it (Khadra.Application/Disputes/Dtos). Both parties and the Admin see the same shape. */
export interface Dispute {
  readonly ticketId: string;
  readonly bookingId: string;
  readonly status: 'Open' | 'UnderReview' | 'Resolved' | 'Withdrawn';
  readonly isLive: boolean;
  readonly openedByParty: 'Customer' | 'Dealer';
  readonly openedByUserId: string;
  readonly openedByName: string;
  readonly reason: string;
  readonly openedAt: string;
  readonly slaDeadline: string;
  readonly isOverdue: boolean;
  readonly assignedAdminId: string | null;
  readonly assignedAdminName: string | null;
  readonly closedAt: string | null;
  readonly statements: readonly DisputeStatement[];
  readonly resolution: DisputeResolution | null;
  /** What a resolution must split, from the server. The console never derives this itself. */
  readonly depositHeld: Money;
  readonly booking: Booking;
}

export interface DisputeStatement {
  readonly statementId: string;
  readonly party: 'Customer' | 'Dealer';
  readonly authorUserId: string;
  readonly authorName: string;
  readonly body: string;
  readonly createdAt: string;
  /** Freshly signed per request; never stored. */
  readonly evidence: readonly {
    readonly fileName: string;
    readonly url: string;
    readonly expiresAt: string;
  }[];
}

/** Recorded, not executed: nothing moves money until the Payments module ships. */
export interface DisputeResolution {
  readonly depositHeld: Money;
  readonly refundToCustomer: Money;
  readonly retainedByPlatform: Money;
  readonly transferredToDealer: Money;
  readonly dealerCharge: Money | null;
  readonly waivesEverything: boolean;
  readonly note: string;
  readonly resolvedByAdminId: string;
  readonly resolvedByName: string;
  readonly resolvedAt: string;
}

export interface EvidenceUpload {
  readonly uploadUrl: string;
  readonly storageKey: string;
  readonly expiresAt: string;
}

/**
 * One row of the Admin's dispute queue (`GET /api/v1/admin/disputes`).
 *
 * `isOverdue` is judged against the deadline frozen when the ticket was opened, so raising the SLA
 * later never retroactively breaches a promise already made.
 */
export interface DisputeListItem {
  readonly ticketId: string;
  readonly bookingId: string;
  readonly bookingReference: string;
  readonly dealerName: string;
  readonly customerName: string;
  readonly openedByParty: 'Customer' | 'Dealer';
  readonly reason: string;
  readonly status: 'Open' | 'UnderReview' | 'Resolved' | 'Withdrawn';
  readonly openedAt: string;
  readonly slaDeadline: string;
  readonly isOverdue: boolean;
  readonly assignedAdminId: string | null;
  readonly assignedAdminName: string | null;
  readonly closedAt: string | null;
  readonly statementCount: number;
}
