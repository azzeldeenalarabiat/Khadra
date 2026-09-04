/**
 * When a promised window counts as "running out".
 *
 * The dealer screens used to call an application at risk with `hours < 12`. That number was right
 * only while the review SLA was 48 hours — it is 0.75 of 48 — and it is an absolute count, so
 * halving the SLA would have left the list calling an application at risk from the moment it arrived,
 * while the dashboard, which computes the same judgement server-side from the frozen window, went on
 * disagreeing with it.
 *
 * Measured as a FRACTION of the window each record froze, so it stays correct whatever the SLA
 * becomes. The threshold matches `AdminDashboard:SlaWarningThreshold`; the two are kept level by
 * hand today. Moving this judgement onto the API (as `AttentionQueueBuilder` already does for the
 * dashboard) is the real fix and is on the pre-launch checklist.
 */
const WARNING_THRESHOLD = 0.75;

/**
 * True once the elapsed part of a frozen window has passed the warning threshold, and it has not yet
 * run out. Past the deadline is not "at risk" — it is breached, which the caller reports as its own,
 * louder state.
 */
export function isAtRisk(startedAtMs: number, deadlineMs: number, nowMs: number): boolean {
  if (!(deadlineMs > startedAtMs)) return false;
  const elapsed = (nowMs - startedAtMs) / (deadlineMs - startedAtMs);
  return elapsed >= WARNING_THRESHOLD && elapsed < 1;
}
