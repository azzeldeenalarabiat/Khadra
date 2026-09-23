import { REQUEST_CONTEXT, RESPONSE_INIT, inject } from '@angular/core';

/**
 * What `server.ts` tells the Angular render about the request it is rendering. Only ever present on
 * the server; in the browser `REQUEST_CONTEXT` is null.
 */
export interface ServerRenderContext {
  /** Where the renderer reaches the API directly (never through the BFF, never with a session). */
  readonly apiBaseUrl: string;
  /** The public origin pages are served from, for canonical URLs and Open Graph. */
  readonly publicBaseUrl: string;
  /**
   * The visitor's address as the customer BFF saw it, or null when the request did not prove it came
   * through the BFF. Passed to the API so each visitor keeps their own rate-limit partition.
   */
  readonly clientAddress: string | null;
}

export function injectServerContext(): ServerRenderContext | null {
  return (inject(REQUEST_CONTEXT, { optional: true }) as ServerRenderContext | null) ?? null;
}

/**
 * Sets the HTTP status of the page being rendered on the server: a real 404 for a car that is not
 * listed, a 503 while the API cannot be reached, so a crawler neither indexes an error as content nor
 * forgets a page that is only briefly unavailable. A no-op in the browser.
 */
export function injectResponseStatus(): (status: number) => void {
  const init = inject(RESPONSE_INIT, { optional: true });
  return (status: number) => {
    if (init) (init as { status?: number }).status = status;
  };
}
