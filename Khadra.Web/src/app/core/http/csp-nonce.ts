/**
 * The nonce that admits Angular's event-replay scripts under the website's content security policy (pre-launch item
 * 222).
 *
 * A server-rendered page runs two inline scripts so that a tap made before hydration is replayed after it: the
 * event-dispatch contract, which the build inlines into `index.html`, and the `__jsaction_bootstrap(…)` call the server
 * adds after it. The customer BFF serves `script-src 'self'`, which refused both, so the tap was lost. The BFF now mints
 * a nonce for each page, names it in that response's `script-src`, and sends it here; the render puts it on those two
 * scripts. Angular adds it to the second itself (`CSP_NONCE`); the first is the build's, and gets it from
 * {@link nonceEventDispatchContract}.
 */

/** The id the build gives the event-dispatch contract it inlines into `index.html`. */
export const EVENT_DISPATCH_SCRIPT_ID = 'ng-event-dispatch-contract';

/** Base64, as the BFF mints it (16 random bytes). Anything else is not a nonce this renderer will print. */
const NONCE = /^[A-Za-z0-9+/]{16,88}={0,2}$/;

/** The nonce a header carried, or null when it is absent or not one. */
export function cspNonceFrom(value: string | null | undefined): string | null {
  return value && NONCE.test(value) ? value : null;
}

/** Puts the page's nonce on the build's event-dispatch contract, when the page still has one. */
export function nonceEventDispatchContract(document: Document, nonce: string | null): void {
  if (!nonce) return;
  document.getElementById(EVENT_DISPATCH_SCRIPT_ID)?.setAttribute('nonce', nonce);
}
