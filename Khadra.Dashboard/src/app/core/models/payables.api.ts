import { Money } from './fleet.api';

/**
 * The office payables ledger (payments Phase 8), as the administrator reads it from `/api/v1/admin/office-*` and
 * `/api/v1/admin/finance/summary`, and the office from `/api/v1/dealers/me/payouts`. Every figure is the server's; a
 * screen renders them and computes none. A NET may be below zero: the office owes Khadra. A field a reader may not
 * see arrives as null for that reader — the office never gets a hold's reason, an administrator's note, a void's
 * reason, the kind of money, or the blocks.
 */

/** One line of a payable: `RentalRevenue`, `DisputeShare`, `PenaltyKept` (+), `Commission`, `DisputeCharge` (−). */
export interface PayableLine {
  readonly kind: string;
  /** Always positive: the kind says which way it goes. */
  readonly amount: Money;
  readonly ticketId: string | null;
}

/** Why a payable is held back: `NeedsReview`, `PenaltyNotWholeDeposit`, `Contradicted` or `Manual`. */
export interface PayableHold {
  readonly holdId: string;
  readonly bookingId: string;
  readonly bookingReference: string | null;
  readonly payableId: string | null;
  readonly reason: string;
  /** The system's description, or the administrator's reason. */
  readonly detail: string | null;
  readonly openedAt: string;
  /** The administrator who held it; null for the system's holds. */
  readonly openedBy: string | null;
}

/** Why an open payable is not due, read live: `RefundOutstanding` or `DisputeLive`. */
export interface PayableBlock {
  readonly kind: string;
  readonly refundId: string | null;
}

export interface SettlementRef {
  readonly settlementId: string;
  readonly number: string;
  /** The Amman day the money moved, `yyyy-MM-dd`. */
  readonly paidOn: string;
}

/**
 * One booking's payable. `outcome`: `Rental`, `RentalAfterDispute`, `DisputeDecided`, `PenaltyKept`,
 * `DepositReleased` or `PaymentReturned`. `state`: `Due`, `NothingDue`, `Blocked`, `OnHold` or `Settled`.
 */
export interface OfficePayable {
  readonly payableId: string;
  readonly bookingId: string;
  readonly bookingReference: string;
  readonly dealerId: string;
  readonly dealerName: string;
  readonly outcome: string;
  readonly state: string;
  readonly officeMoney: Money;
  readonly commission: Money;
  readonly officeCharges: Money;
  /** Signed: below zero, the office owes. */
  readonly net: Money;
  readonly finalAt: string;
  readonly recordedAt: string;
  readonly lines: readonly PayableLine[];
  readonly settlement: SettlementRef | null;
  /** Administrator only. */
  readonly isTest: boolean | null;
  readonly calculatorVersion: number | null;
  readonly holds: readonly PayableHold[] | null;
  readonly blocks: readonly PayableBlock[] | null;
}

/** The newest settlement that stands. `direction`: `Payout`, `Received` or `Netted`. */
export interface SettlementSummary {
  readonly settlementId: string;
  readonly number: string;
  readonly direction: string;
  /** Signed from Khadra's side. */
  readonly amount: Money;
  readonly paidOn: string;
}

/** An office's balance in one currency and one kind of money. */
export interface OfficeBalance {
  readonly dealerId: string;
  readonly dealerName: string;
  readonly currency: string;
  /** The kind of money, sent back when settling. Administrator only. */
  readonly provider: string | null;
  readonly isTest: boolean | null;
  readonly dueCount: number;
  /** Due now, netted: above zero Khadra owes the office. */
  readonly due: Money;
  /** Held back or blocked, netted together. Kept as it was; the two parts follow. */
  readonly notYetDueCount: number;
  readonly notYetDue: Money;
  readonly lastSettlement: SettlementSummary | null;
  /** Of those, the ones held back, netted on their own (Wave 4, F56 a). Optional, as the API made it. */
  readonly heldCount?: number;
  readonly held?: Money;
  /** Of those, the ones blocked: a refund outstanding or a dispute live. */
  readonly blockedCount?: number;
  readonly blocked?: Money;
}

export interface OfficeSettlementVoid {
  readonly voidedAt: string;
  /** Administrator only. */
  readonly voidedBy: string | null;
  readonly reason: string | null;
}

/** A settlement recorded by hand. */
export interface OfficeSettlement {
  readonly settlementId: string;
  readonly number: string;
  readonly dealerId: string;
  readonly dealerName: string;
  readonly direction: string;
  readonly amount: Money;
  readonly paidOn: string;
  readonly reference: string | null;
  /** Administrator only. */
  readonly note: string | null;
  readonly recordedAt: string;
  /** Administrator only. */
  readonly recordedBy: string | null;
  readonly payableCount: number;
  readonly isTest: boolean | null;
  readonly void: OfficeSettlementVoid | null;
}

export interface OfficeSettlementLine {
  readonly payableId: string;
  readonly bookingId: string;
  readonly bookingReference: string;
  readonly outcome: string;
  readonly net: Money;
}

export interface OfficeSettlementDetail {
  readonly settlement: OfficeSettlement;
  readonly lines: readonly OfficeSettlementLine[];
}

/** Khadra's own money over a span, in one currency and one kind of money. */
export interface FinanceTotals {
  readonly currency: string;
  readonly isTest: boolean;
  readonly commissionEarned: Money;
  /** What resolved disputes left with the platform: beside commission, never inside it. */
  readonly keptFromDisputes: Money;
  readonly officeMoney: Money;
  readonly officeCharges: Money;
  readonly paidToOffices: Money;
  readonly receivedFromOffices: Money;
  readonly owedToOffices: Money;
  readonly owedByOffices: Money;
  readonly payablesRecorded: number;
}

export interface FinanceSummary {
  /** The span's first and last Amman days, `yyyy-MM-dd`, inclusive. */
  readonly from: string;
  readonly to: string;
  readonly totals: readonly FinanceTotals[];
  readonly heldBookings: number;
  readonly heldPayables: number;
  /** Both of the above, as the server counts them: what the banner states. */
  readonly held: number;
  readonly blockedPayables: number;
}

/** The office's own payouts page. */
export interface OfficePayouts {
  readonly balances: readonly OfficeBalance[];
}
