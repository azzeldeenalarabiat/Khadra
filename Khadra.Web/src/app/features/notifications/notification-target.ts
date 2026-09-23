/**
 * Where a customer notification leads, as the app routes it: every customer kind is about a
 * booking, whose id is the subject — except a dispute update, whose subject is the dispute ticket.
 * Null when there is nothing to open.
 */
export function notificationTarget(
  kind: string,
  subjectId: string | null | undefined,
): readonly ['bookings' | 'disputes', string] | null {
  if (!subjectId) return null;
  return [kind === 'YourDisputeUpdated' ? 'disputes' : 'bookings', subjectId];
}
