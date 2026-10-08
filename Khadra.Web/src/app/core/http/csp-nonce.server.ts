import { CSP_NONCE, DOCUMENT, EnvironmentProviders, inject, makeEnvironmentProviders } from '@angular/core';
import { BEFORE_APP_SERIALIZED } from '@angular/platform-server';
import { nonceEventDispatchContract } from './csp-nonce';
import { injectServerContext } from './server-context';

/**
 * The page's nonce, on both event-replay scripts (pre-launch item 222). Angular prints `CSP_NONCE` on the
 * `__jsaction_bootstrap(…)` call it adds; the build's event-dispatch contract gets it just before the page is
 * serialised, after Angular has decided whether the page keeps one. Server only: the browser never sees the token.
 */
export function provideCspNonce(): EnvironmentProviders {
  return makeEnvironmentProviders([
    { provide: CSP_NONCE, useFactory: () => injectServerContext()?.cspNonce ?? null },
    {
      provide: BEFORE_APP_SERIALIZED,
      multi: true,
      useFactory: () => {
        const document = inject(DOCUMENT);
        const nonce = inject(CSP_NONCE, { optional: true });
        return () => nonceEventDispatchContract(document, nonce);
      },
    },
  ]);
}
