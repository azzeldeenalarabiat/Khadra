import { Booking } from './bookings.api';
import { Money } from './fleet.api';
import { PayableLine } from './payables.api';

/**
 * A dispute as the API returns it to the administrator (Khadra.Application/Disputes/Dtos). The rental
 * office receives the same ticket with its own copy of the decision: `OfficeDispute`.
 */
export interface Dispute {
  readonly ticketId: string;
  readonly bookingId: string;
  readonly status: 'Open' | 'UnderReview' | 'Resolved' | 'Withdrawn';
  readonly isLive: boolean;
  readonly openedByParty: 'Customer' | 'Dealer';
  readonly openedByUserId: string;
  /**
   * The opener's name. When `openedByAccountClosed` is true this holds an English stand-in kept only
   * for older customer apps: never display it then.
   */
  readonly openedByName: string;
  /** True exactly when the opener's account no longer resolves (closed). */
  readonly openedByAccountClosed: boolean;
  readonly reason: string;
  readonly openedAt: string;
  readonly slaDeadline: string;
  readonly isOverdue: boolean;
  /** Null while nobody holds the ticket. */
  readonly assignedAdminId: string | null;
  /**
   * Null exactly while nobody holds the ticket. When `assignedAdminAccountClosed` is true this holds
   * an English stand-in: never display it then.
   */
  readonly assignedAdminName: string | null;
  /** True exactly when the ticket is held by an account that no longer resolves (closed). */
  readonly assignedAdminAccountClosed: boolean;
  readonly closedAt: string | null;
  readonly statements: readonly DisputeStatement[];
  readonly resolution: DisputeResolution | null;
  /**
   * What a resolution must split, from the server. The console never derives this itself. On a live
   * ticket it is what earlier disputes on the booking LEFT (item 169); on a resolved one, the basis it
   * was decided against.
   */
  readonly depositHeld: Money;
  readonly booking: Booking;
  /**
   * The deposit the platform held for disputes before any was resolved (zero once the whole payment
   * went back or the window released it). Added 2026-09-26; absent from an older API.
   */
  readonly depositOnBooking?: Money;
  /**
   * What the booking's EARLIER resolved disputes already decided: zero on a first dispute. Added
   * 2026-09-26; absent from an older API. Shown, never subtracted by the console.
   */
  readonly decidedByEarlierTickets?: Money;
  /**
   * What the booking's EARLIER resolved disputes charged the office: zero when none did. The office
   * charge on this ticket is bounded by the booking's assessed range less this (E2E F34). Added
   * 2026-10-05, the administrator's copy only: null for the parties, absent from an older API.
   */
  readonly chargedToDealerEarlier?: Money | null;
  /**
   * Where the ticket stands against its SLA, by the work queue's own rule (E2E F42). Added 2026-10-05,
   * the administrator's copy only: null for the parties, absent from an older API.
   */
  readonly slaState?: DisputeSlaState | null;
}

/** `DisputeSlaStates` on the server. A state this build does not know reads as neutral. */
export type DisputeSlaState = 'Closed' | 'OnTime' | 'AtRisk' | 'Overdue';

export interface DisputeStatement {
  readonly statementId: string;
  readonly party: 'Customer' | 'Dealer';
  readonly authorUserId: string;
  /**
   * The author's name. When `authorAccountClosed` is true this holds an English stand-in kept only
   * for older customer apps: never display it then.
   */
  readonly authorName: string;
  /** True exactly when the author's account no longer resolves (closed). */
  readonly authorAccountClosed: boolean;
  readonly body: string;
  readonly createdAt: string;
  /** Freshly signed per request; never stored. */
  readonly evidence: readonly {
    readonly fileName: string;
    readonly url: string;
    readonly expiresAt: string;
  }[];
}

/**
 * The administrator's copy of a decision: every share. The customer's share is refunded automatically;
 * what the platform keeps and what goes to the office are settled by hand until a payout rail exists.
 */
export interface DisputeResolution {
  readonly depositHeld: Money;
  readonly refundToCustomer: Money;
  readonly retainedByPlatform: Money;
  readonly transferredToDealer: Money;
  readonly dealerCharge: Money | null;
  readonly waivesEverything: boolean;
  readonly note: string;
  readonly resolvedByAdminId: string;
  /**
   * The resolving administrator's name. When `resolvedByAccountClosed` is true this holds an English
   * stand-in: never display it then.
   */
  readonly resolvedByName: string;
  /** True exactly when the resolving administrator's account no longer resolves (closed). */
  readonly resolvedByAccountClosed: boolean;
  readonly resolvedAt: string;
}

/**
 * The rental office's copy of a decision (owner decision 3, 2026-09-26; pre-launch item 151): the basis
 * the decision split, the office's own share and any charge assessed to it. The server sends the
 * customer's refund, the platform's share and the waiver flag (read from the platform's share) as null
 * to the office, and this type leaves them out, so no office screen can show them.
 */
export type OfficeDisputeResolution = Omit<
  DisputeResolution,
  'refundToCustomer' | 'retainedByPlatform' | 'waivesEverything'
>;

/** A dispute as the rental office receives it: the same ticket, with the office's copy of the decision. */
export type OfficeDispute = Omit<Dispute, 'resolution'> & {
  readonly resolution: OfficeDisputeResolution | null;
  /**
   * What a decided dispute comes to for the office's money (Wave 2 C1): projected by the ledger's own office function
   * until the payouts ledger records the booking, the recorded payable after. Null while the ticket is undecided, on
   * a booking nothing was paid online for, and from an older API.
   */
  readonly expectedOutcome?: OfficeExpectedOutcome | null;
};

/** `Projected` while no payable exists; `Recorded` once the payouts ledger recorded one. */
export interface OfficeExpectedOutcome {
  readonly source: 'Projected' | 'Recorded' | string;
  readonly payableId: string | null;
  readonly outcome: string;
  readonly officeMoney: Money;
  readonly commission: Money;
  readonly charges: Money;
  /** Signed: below zero, the office owes. */
  readonly net: Money;
  readonly lines: readonly PayableLine[];
  readonly finalAt: string;
  /** While a cancellation's or a no-show's dispute window is open, when it closes; another dispute may change this. */
  readonly furtherDecisionsPossibleUntil: string | null;
  /**
   * Another dispute on the booking is open right now, so its decision can still change these projected figures, even
   * after the window closed. Added after the advisor's review of Wave 2; absent from an older API.
   */
  readonly anotherDisputeOpen?: boolean;
}

/**
 * What a decision WOULD do, before it is made (Wave 2 C1; E2E F37), from
 * `POST /api/v1/admin/disputes/{id}/resolution-preview`. Nothing is written to produce it. It states what the records
 * will say, never what money will do: the customer's refund is requested, the office is paid only when settled, and
 * nothing is final while a cancellation's dispute window is open.
 */
export interface ResolutionPreview {
  /** The booking's status once decided: `Completed` for a returned booking, else unchanged. */
  readonly statusAfter: string;
  /** `Final`, or `NotApplicable` when nothing was paid online. */
  readonly officeState: string;
  readonly outcome: string | null;
  readonly lines: readonly PayableLine[];
  readonly customer: { readonly refundRequested: Money };
  readonly platform: { readonly retainedShare: Money; readonly commission: Money };
  readonly office: {
    readonly share: Money;
    readonly money: Money;
    readonly frozenCommission: Money;
    readonly commission: Money;
    readonly charges: Money;
    /** Signed: below zero, the office owes. */
    readonly net: Money;
  };
  readonly earlierDecisions: {
    readonly count: number;
    readonly toCustomer: Money;
    readonly keptByPlatform: Money;
    readonly toOffice: Money;
    readonly chargedToOffice: Money;
  } | null;
  readonly recordedNotBefore: string;
  readonly furtherDecisionsPossibleUntil: string | null;
  readonly calculatorVersion: number;
  /**
   * Where the booking's records already contradict one another (`financialIssue` codes): the payouts ledger HOLDS such a
   * booking for review instead of recording it, whatever is decided. Empty when they agree. Added after the advisor's
   * review of Wave 2; absent from an older API.
   */
  readonly ledgerIssues?: readonly string[];
  /** The booking is final and past the ledger's margin already, so its next pass records it (the server's clock). */
  readonly recordedAtNextPass?: boolean;
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
  /** Null only if the booking cannot be read, which cannot happen (bookings are never deleted). */
  readonly bookingReference: string | null;
  /** Null when the dealership no longer resolves (no longer on the platform). */
  readonly dealerName: string | null;
  /** Null when the customer's account no longer resolves (closed). */
  readonly customerName: string | null;
  readonly openedByParty: 'Customer' | 'Dealer';
  readonly reason: string;
  readonly status: 'Open' | 'UnderReview' | 'Resolved' | 'Withdrawn';
  readonly openedAt: string;
  readonly slaDeadline: string;
  readonly isOverdue: boolean;
  /** Null while nobody holds the ticket. */
  readonly assignedAdminId: string | null;
  /**
   * Null when the ticket is unassigned (`assignedAdminId` is null) AND when the holder's account no
   * longer resolves (`assignedAdminId` is set). Branch on the id to tell the two apart.
   */
  readonly assignedAdminName: string | null;
  readonly closedAt: string | null;
  readonly statementCount: number;
}

/** How the queue is shaped under the filters in force — all of it, not the page on screen. */
export interface DisputeQueueCounts {
  readonly total: number;
  readonly overdue: number;
  readonly unassigned: number;
}
