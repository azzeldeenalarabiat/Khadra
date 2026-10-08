/**
 * What an audit entry was taken on, as the facts that word it.
 *
 * Every entry carries a subject label snapshotted when it was written, and for most kinds that label
 * IS a fact: a dealership's name, an administrator's name, a booking reference, a city. Two kinds were
 * written as English sentences instead, into a table that refuses UPDATE — "Dispute on KH-NY8AHLNK"
 * until 2026-09-27, and "Customer 0198abcd" — and printed as stored, they put English inside every
 * Arabic line about them. Those two are worded from facts the entry carries beside its label: the
 * booking a dispute is about, which the server reads through the ticket, and the customer's own id.
 * Nothing is read back out of the sentence.
 *
 * Shared by the dashboard's activity strip and the audit log, so the two cannot describe one entry two
 * ways. Each words the result for itself: the strip inside a sentence, the log in a table cell.
 */
export type AuditSubject =
  /** A dispute, named by the booking it is about. */
  | { readonly kind: 'dispute'; readonly reference: string }
  /** A booking, by its reference. */
  | { readonly kind: 'booking'; readonly reference: string }
  /** A customer, by the short reference the audit trail has always used for one — never a name. */
  | { readonly kind: 'customer'; readonly reference: string }
  /** A published legal text, by its document's kind name (Wave 2 G1): a code, worded by each screen. */
  | { readonly kind: 'legal'; readonly document: string }
  /** Anything else, as it was recorded: a name somebody typed, or a label from a newer server. */
  | { readonly kind: 'label'; readonly label: string };

/** The part of either wire shape that says what an entry was taken on. */
export interface AuditSubjectFacts {
  readonly entityType: string;
  readonly entityId: string | null;
  readonly bookingReference: string | null;
  readonly subjectLabel: string;
}

export function auditSubject(entry: AuditSubjectFacts): AuditSubject {
  const reference = entry.bookingReference;
  if (entry.entityType === 'Dispute' && reference) return { kind: 'dispute', reference };
  if (entry.entityType === 'Booking' && reference) return { kind: 'booking', reference };
  if (entry.entityType === 'LegalDocument') return { kind: 'legal', document: entry.subjectLabel };
  if (entry.entityType === 'Customer' && entry.entityId) {
    return { kind: 'customer', reference: customerReference(entry.entityId) };
  }
  // Also where a fact is missing, which the server rules out: the label as it was stored beats a
  // reading of it.
  return { kind: 'label', label: entry.subjectLabel };
}

/** The value a subject contributes to a sentence: the reference, or the label as recorded. */
export const subjectValue = (subject: AuditSubject): string =>
  subject.kind === 'label' ? subject.label : subject.kind === 'legal' ? subject.document : subject.reference;

/**
 * A customer's short reference: the first eight hex digits of their id — the characters the server
 * has always written after "Customer" in the label, and the way this console shortens every other id.
 * The audit trail never names a customer, because it can never be erased; the id links to the profile.
 */
export const customerReference = (id: string): string => id.replace(/-/g, '').slice(0, 8);

/** The part of either wire shape that says who acted. */
export interface AuditActorFacts {
  /** Null when nobody acted: the first administrator's invitation, a background job. */
  readonly actorUserId: string | null;
  readonly actorName: string;
}

/**
 * Who acted, in the reader's language (pre-launch item 175).
 *
 * An entry nobody acted on is stored with the English name "System", and both screens printed it: in the audit log's
 * Who column and its filter, and inside the strip's Arabic sentence ("… من قِبل System"). It is recognised by having
 * no actor id, never by its name — a person may be called System. Everyone else is named as the entry recorded them,
 * which is a snapshot taken when they acted.
 */
export const auditActorName = (entry: AuditActorFacts, t: (key: 'auditLog.systemActor') => string): string =>
  entry.actorUserId === null ? t('auditLog.systemActor') : entry.actorName;
