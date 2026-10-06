import { Booking } from '../../core/api/bookings.api';

/**
 * Whether the customer's handover code is worth showing yet (Wave 3 D4; owner, 2026-10-05).
 *
 * The rental office cannot record a pickup before the booking's `pickupAvailableFrom` — the rental start less the
 * turnaround frozen on it — nor a return before `returnAvailableFrom`, the rental start. The server still issues a
 * code at any time while the booking is confirmed, because installed apps ask for one on every confirmed booking
 * (owner, 2026-10-06); this page simply does not offer one the office could not use, and says when it will. The
 * moments are the SERVER's; nothing here works them out.
 */
export interface HandoverWindow {
  /** Which code the booking is waiting for, or null when it waits for none. */
  readonly kind: 'Pickup' | 'Return' | null;
  /** True when the code can be used now, or when the server sent no moment (an older API, with no window). */
  readonly open: boolean;
  /** The moment the code becomes usable, while it is still ahead; null once it is open. */
  readonly opensAt: string | null;
}

export function handoverWindow(booking: Booking | null | undefined, now: number): HandoverWindow {
  const kind = booking?.status === 'Confirmed' ? 'Pickup' : booking?.status === 'PickedUp' ? 'Return' : null;
  if (!booking || !kind) return { kind: null, open: false, opensAt: null };
  const moment = kind === 'Pickup' ? booking.pickupAvailableFrom : booking.returnAvailableFrom;
  if (!moment) return { kind, open: true, opensAt: null };
  const at = Date.parse(moment);
  if (Number.isNaN(at) || at <= now) return { kind, open: true, opensAt: null };
  return { kind, open: false, opensAt: moment };
}

/** The longest a page waits on a timer for a code to open; beyond that a reload is the honest answer. */
const LONGEST_WAIT_MS = 24 * 60 * 60 * 1000;

/** How long until the window opens, for a one-shot timer; null when it is open or too far away to wait for. */
export function msUntilOpen(window: HandoverWindow, now: number): number | null {
  if (window.open || !window.opensAt) return null;
  const wait = Date.parse(window.opensAt) - now;
  return wait > 0 && wait <= LONGEST_WAIT_MS ? wait : null;
}
