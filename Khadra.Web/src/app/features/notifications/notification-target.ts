/**
 * Where a customer notification leads, as the app routes it: every customer kind is about a
 * booking, whose id is the subject — except a dispute update, whose subject is the dispute ticket,
 * and a rejected document, which is about the account and has no subject at all (Wave 4, W4-9): it
 * opens the customer's documents. Null when there is nothing to open.
 */
export function notificationTarget(
  kind: string,
  subjectId: string | null | undefined,
): readonly ['bookings' | 'disputes', string] | readonly ['profile', 'documents'] | null {
  if (kind === 'YourDocumentRejected') return ['profile', 'documents'];
  if (!subjectId) return null;
  return [kind === 'YourDisputeUpdated' ? 'disputes' : 'bookings', subjectId];
}
