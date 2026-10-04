/**
 * Whether search engines may index this copy of the website.
 *
 * Only a renderer told so — `KHADRA_INDEXABLE=true`, which only Production sets — is indexable. Anything else,
 * unset included, is closed: a Staging copy was open to every crawler, with a sitemap pointing at it, where it could
 * compete in search with the real site and show test cars and test prices to the public (E2E F3). Closed is the
 * safe default — a forgotten variable costs a staging copy nothing, and costs Production only a launch step, which
 * `docs/production.md` lists.
 */
export function indexableFrom(value: string | undefined): boolean {
  return value?.trim().toLowerCase() === 'true';
}

/** The `X-Robots-Tag` every response of a closed copy carries, pages, files and redirects alike. */
export const CLOSED_ROBOTS_TAG = 'noindex, nofollow';

/**
 * `robots.txt`. An indexable site keeps the API, the BFF and the account pages out and names its sitemap; a closed
 * copy disallows everything and names no sitemap, so nothing points a crawler at it.
 */
export function robotsTxt(indexable: boolean, publicBaseUrl: string, privatePages: readonly string[]): string {
  if (!indexable) return ['User-agent: *', 'Disallow: /', ''].join('\n');
  return [
    'User-agent: *',
    'Disallow: /api/',
    'Disallow: /bff/',
    ...privatePages.flatMap((page) => ['ar', 'en'].map((language) => `Disallow: /${language}/${page.replace('/**', '/')}`)),
    '',
    `Sitemap: ${publicBaseUrl}/sitemap.xml`,
    '',
  ].join('\n');
}
