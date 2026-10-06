import { safeReturnUrl } from './auth.guards';

/** The query parameter that carries a heart pressed while signed out (Wave 3 E5; E2E F12). */
export const SAVE_INTENT_PARAM = 'save';

const KEY = 'kh.returnAddress';
/**
 * How long a remembered return address is followed: long enough to open the verification email and sign in, short
 * enough that a later, unrelated sign-in on the same browser does not land somewhere stale. A convenience of this
 * browser, not a rule of the platform.
 */
const LIFETIME_MS = 24 * 60 * 60 * 1000;

/** The same site path, carrying the car to save once the visitor is signed in. */
export function withSaveIntent(url: string, vehicleId: string): string {
  const [path, query = ''] = url.split('?');
  const params = new URLSearchParams(query);
  params.set(SAVE_INTENT_PARAM, vehicleId);
  return `${path}?${params.toString()}`;
}

/**
 * Remembers where to come back to across the email-verification step, which opens in a new tab with nothing of the
 * first one. Only a site path is kept — never the saved list, which lives on the account (owner, 2026-09-11) — and a
 * browser that refuses storage simply loses the convenience.
 */
export function rememberReturnAddress(storage: Storage | null | undefined, url: string, now: number = Date.now()): void {
  try {
    storage?.setItem(KEY, JSON.stringify({ url, at: now }));
  } catch {
    // Private mode, or storage refused: the visitor signs in and lands on the home page, as before.
  }
}

/** The remembered return address, while it is fresh and still a path on this site; null otherwise. */
export function recallReturnAddress(
  storage: Storage | null | undefined,
  language: string,
  now: number = Date.now(),
): string | null {
  try {
    const raw = storage?.getItem(KEY);
    if (!raw) return null;
    const { url, at } = JSON.parse(raw) as { url?: unknown; at?: unknown };
    if (typeof url !== 'string' || typeof at !== 'number' || now - at > LIFETIME_MS) return null;
    const safe = safeReturnUrl(url, language);
    return safe === url ? url : null;
  } catch {
    return null;
  }
}

export function forgetReturnAddress(storage: Storage | null | undefined): void {
  try {
    storage?.removeItem(KEY);
  } catch {
    // Nothing to do: storage was never usable.
  }
}
