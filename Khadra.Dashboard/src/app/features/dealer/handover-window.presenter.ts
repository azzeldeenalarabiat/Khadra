/**
 * Whether the office may record a pickup or a return yet (Wave 3 D4; owner, 2026-10-05).
 *
 * The SERVER sends each moment — `pickupAvailableFrom`, the rental start less the turnaround frozen on
 * the booking, and `returnAvailableFrom`, the rental start — and refuses a handover recorded before it
 * (`booking.pickup_too_early`, `booking.return_too_early`). The console works none of it out: it only
 * keeps the button disabled, with the moment beside it, until this browser's clock reaches what the
 * server sent. A clock that is behind can only keep the button closed a little longer; one that is
 * ahead opens it early, and the server's refusal, worded in the dialog, still says no.
 */
export interface HandoverWindow {
  /** True once the moment has passed, or when the server sent none (an older API, with no window). */
  readonly open: boolean;
  /** The moment, while it is still ahead; null once the window is open. */
  readonly opensAt: string | null;
}

export function handoverWindow(moment: string | null | undefined, now: number): HandoverWindow {
  if (!moment) return { open: true, opensAt: null };
  const at = Date.parse(moment);
  if (Number.isNaN(at) || at <= now) return { open: true, opensAt: null };
  return { open: false, opensAt: moment };
}

/** The longest a page waits on a timer for a window to open; past that, a reload is the honest answer. */
const LONGEST_WAIT_MS = 24 * 60 * 60 * 1000;

/**
 * How long until the soonest of these windows opens, for a one-shot timer that re-reads the clock
 * then — so a page left open over the moment enables its button without a reload. Null when nothing is
 * ahead, or when the soonest is further away than a page is reasonably left open.
 */
export function msUntilNextOpening(windows: readonly HandoverWindow[], now: number): number | null {
  const ahead = windows
    .map((window) => (window.opensAt ? Date.parse(window.opensAt) - now : Number.NaN))
    .filter((wait) => Number.isFinite(wait) && wait > 0);
  if (ahead.length === 0) return null;
  const soonest = Math.min(...ahead);
  return soonest <= LONGEST_WAIT_MS ? soonest : null;
}
