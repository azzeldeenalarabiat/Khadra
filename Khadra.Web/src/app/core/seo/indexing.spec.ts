import { describe, expect, it } from 'vitest';
import { PRIVATE_PAGES } from '../../app.routes.server';
import { indexableFrom, robotsTxt } from './indexing';

/** A Staging copy of the website was open to every search engine, with a sitemap pointing at it (E2E F3). */
describe('whether the website may be indexed', () => {
  it('is closed unless the renderer is told otherwise, in so many words', () => {
    expect(indexableFrom(undefined)).toBe(false);
    expect(indexableFrom('')).toBe(false);
    expect(indexableFrom('false')).toBe(false);
    expect(indexableFrom('yes')).toBe(false);
    expect(indexableFrom('true')).toBe(true);
    expect(indexableFrom(' TRUE ')).toBe(true);
  });

  it('disallows everything and names no sitemap on a closed copy', () => {
    const closed = robotsTxt(false, 'https://khadra-web-staging.example', PRIVATE_PAGES);

    expect(closed).toBe('User-agent: *\nDisallow: /\n');
    expect(closed).not.toContain('Sitemap');
  });

  it('keeps the API, the BFF and every account page out of an indexable site, and names its sitemap', () => {
    const open = robotsTxt(true, 'https://www.example.jo', PRIVATE_PAGES);

    expect(open).toContain('Disallow: /api/');
    expect(open).toContain('Disallow: /bff/');
    expect(open).toContain('Disallow: /ar/invoices/');
    expect(open).toContain('Disallow: /en/invoices/');
    expect(open).toContain('Sitemap: https://www.example.jo/sitemap.xml');
    expect(open).not.toContain('Disallow: /\n');
  });
});
