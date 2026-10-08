import { EN, TranslationKey } from './en';

/**
 * Which reader a status is being named for, when the plain word is not the right one.
 *
 * - `booking`: `Approved` on a booking is a gallery saying yes; on a dealership it is a licence check
 *   that passed, and Arabic does not use one word for both.
 * - `dealerBooking`: the rental office's own wording for its queue — a `Requested` booking is
 *   "Pending" to the office that has to answer it, and `PickedUp` is an "Active" rental.
 * - `vehicle`: a car in a fleet — `Active` is "Published" (customers can find it), `Maintenance` is
 *   "Off the road"; an `Active` dealership or account is a different word.
 * - `financialDocument`: an issued document's standing (payments Phase 5b) — `Current`, `Superseded`
 *   ("Earlier version") and `Voided`, words no other record uses.
 * - `financialDocumentEmail`: a receipt's email to its customer (payments Phase 7) — `Queued`, `Sent`,
 *   `Skipped` and `Failed`, where `Sent` means accepted by the mail provider.
 * - `customerDocument`: a renter's licence or ID on file (Wave 5, F90). `PendingReview` is what every upload
 *   is born as and stays, because nothing on this platform reviews one — an administrator can only reject it —
 *   so it is "Uploaded", the customer website's word, and not a dealership application's "Pending review".
 */
export type StatusScope =
  | 'booking'
  | 'dealerBooking'
  | 'vehicle'
  | 'financialDocument'
  | 'financialDocumentEmail'
  | 'customerDocument';

/**
 * The dictionary key for a server status name, most specific scope first.
 *
 * A chain rather than one scoped key: the dealer scope only overrides the handful of statuses it
 * words differently, and everything else falls through to the booking wording and then the plain
 * one — so `Rejected` on a dealer's booking still gets the booking sense of the word.
 */
export function statusKey(name: string, scope?: StatusScope): TranslationKey | null {
  const camel = name.charAt(0).toLowerCase() + name.slice(1);
  const chain =
    scope === 'dealerBooking'
      ? [`status.${camel}DealerBooking`, `status.${camel}Booking`, `status.${camel}`]
      : scope === 'booking'
        ? [`status.${camel}Booking`, `status.${camel}`]
        : scope === 'vehicle'
          ? [`status.${camel}Vehicle`, `status.${camel}`]
          : scope === 'financialDocument'
            ? [`status.${camel}FinancialDocument`, `status.${camel}`]
            : scope === 'financialDocumentEmail'
              ? [`status.${camel}FinancialDocumentEmail`, `status.${camel}`]
              : scope === 'customerDocument'
                ? [`status.${camel}CustomerDocument`, `status.${camel}`]
                : [`status.${camel}`];
  const key = chain.find((candidate) => candidate in EN);
  return key === undefined ? null : (key as TranslationKey);
}

/**
 * Server enums that are not statuses, each worded under its own dictionary family.
 *
 * - `party`: `BookingParty` — who acted, who a penalty is attributed to (Customer, Dealer, System,
 *   Unattributed, Admin).
 * - `handoverType`: `HandoverType` — Pickup, Return.
 * - `penaltyReason`: `PenaltyReason` — the stable code for a system-written penalty sentence.
 * - The Payments context (payments Phase 4b): a payment's status and purpose, a refund's status, the
 *   refund progress of a payment, Khadra's commission state, what became of a provider event, the
 *   contradictions a booking's records can show, and the platform's payment mode.
 * - Issued financial documents (payments Phase 5b): a document's type, what issued it, and why a
 *   document owed is on hold.
 * - Their emails (payments Phase 7): what a queued email is waiting for, and what an attempt came to.
 * - The office payables ledger (payments Phase 8): how a paid booking ended for its office, where its payable
 *   stands, the kinds of line it is made of, which way a settlement's money went, why a payable is held back, and
 *   why an open one is not due.
 * - Capture incidents (Wave 4, B1): what a capture notice the platform could not account for is.
 */
export type EnumFamily =
  | 'paymentIncidentKind'
  | 'payableOutcome'
  | 'payableState'
  | 'payableLineKind'
  | 'settlementDirection'
  | 'payableHoldReason'
  | 'payableBlock'
  | 'party'
  | 'handoverType'
  | 'penaltyReason'
  | 'paymentStatus'
  | 'paymentPurpose'
  | 'refundStatus'
  | 'refundProgress'
  | 'commissionState'
  | 'providerEventOutcome'
  | 'financialIssue'
  | 'paymentMode'
  | 'financialDocumentType'
  | 'financialDocumentCause'
  | 'financialDocumentHoldReason'
  | 'financialDocumentEmailWait'
  | 'financialDocumentEmailOutcome';

/** The dictionary key for a server enum name within its family, or null when this build has none. */
export function enumKey(family: EnumFamily, name: string): TranslationKey | null {
  const key = `${family}.${name.charAt(0).toLowerCase()}${name.slice(1)}`;
  return key in EN ? (key as TranslationKey) : null;
}

/**
 * A status this build has no word for, spelled out from its name: "PartiallyRefunded" becomes
 * "Partially refunded".
 *
 * A server enum can grow a member without asking the console, and a blank pill tells the person
 * deciding whether to approve a booking nothing at all. English words are wrong under Arabic, but
 * they are readable and they are true; `I18nService` isolates them so they do not reorder the
 * sentence around them.
 */
export function spellEnumName(name: string): string {
  const spaced = name.replace(/([a-z])([A-Z])/g, '$1 $2');
  return spaced.charAt(0) + spaced.slice(1).toLowerCase();
}
