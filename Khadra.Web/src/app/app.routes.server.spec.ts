import { RenderMode } from '@angular/ssr';
import { describe, expect, it } from 'vitest';
import { PRIVATE_PAGES, serverRoutes } from './app.routes.server';

/**
 * An account's pages are never rendered by the server and never indexed: `PRIVATE_PAGES` also drives
 * `X-Robots-Tag`, `no-store` and `robots.txt` in server.ts. Pinned for the pages added since the list
 * was first written.
 */
describe('private pages', () => {
  it('keep Invoices & Receipts and every document out of the server renderer, in both languages', () => {
    for (const page of ['invoices', 'invoices/**'] as const) {
      expect(PRIVATE_PAGES).toContain(page);
      for (const language of ['ar', 'en'])
        expect(serverRoutes).toContainEqual({ path: `${language}/${page}`, renderMode: RenderMode.Client });
    }
  });
});
