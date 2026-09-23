import { slugFor } from './app/core/routing/slug';

/**
 * The sitemap, built from the same public endpoints the pages read — `GET /vehicles` and
 * `GET /galleries` — so it lists exactly what the catalogue would show and nothing it would 404. No
 * `lastmod`: the API publishes no "last changed" time for a car or an office, and a created date
 * passed off as one would be an invented figure.
 *
 * One index, three files (pages, cars, offices). Each file holds far fewer than the 50,000 URLs a
 * sitemap may carry; if the catalogue ever grows past that, the cars file is the one to split.
 */

const LANGUAGES = ['ar', 'en'] as const;
const PAGE_SIZE = 100;
// A guard against a runaway loop, not a catalogue limit: `hasNext` ends the walk long before it.
const MAX_PAGES = 400;

interface Paged<T> {
  readonly items: readonly T[];
  readonly hasNext: boolean;
}

export async function sitemapIndex(publicBaseUrl: string): Promise<string> {
  const files = ['sitemap-pages.xml', 'sitemap-cars.xml', 'sitemap-offices.xml'];
  return (
    '<?xml version="1.0" encoding="UTF-8"?>\n<sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n' +
    files.map((file) => `  <sitemap><loc>${escape(`${publicBaseUrl}/${file}`)}</loc></sitemap>`).join('\n') +
    '\n</sitemapindex>\n'
  );
}

export async function pagesSitemap(publicBaseUrl: string, apiBaseUrl: string): Promise<string> {
  const cities = await getJson<{ id: string; isActive: boolean }[]>(`${apiBaseUrl}/api/v1/cities`);
  const paths = ['', 'cars', 'dealers', ...cities.filter((city) => city.isActive).map((city) => `cars?city=${city.id}`)];
  return urlset(publicBaseUrl, paths);
}

export async function carsSitemap(publicBaseUrl: string, apiBaseUrl: string): Promise<string> {
  const cars = await everyPage<{ vehicleId: string; make: string; model: string; year: number }>(
    `${apiBaseUrl}/api/v1/vehicles`,
  );
  return urlset(publicBaseUrl, cars.map((car) => `cars/${slugFor(car.vehicleId, car.make, car.model, car.year)}`));
}

export async function officesSitemap(publicBaseUrl: string, apiBaseUrl: string): Promise<string> {
  const offices = await everyPage<{ dealerId: string; businessName: string }>(`${apiBaseUrl}/api/v1/galleries`);
  return urlset(publicBaseUrl, offices.map((office) => `dealers/${slugFor(office.dealerId, office.businessName)}`));
}

/** Each page in both languages, each naming the other as its alternate. */
function urlset(publicBaseUrl: string, paths: readonly string[]): string {
  const entries = paths.flatMap((path) =>
    LANGUAGES.map((language) => {
      const alternates = [...LANGUAGES.map((other) => [other, other] as const), ['x-default', 'ar'] as const]
        .map(([hreflang, target]) => `    <xhtml:link rel="alternate" hreflang="${hreflang}" href="${escape(url(publicBaseUrl, target, path))}"/>`)
        .join('\n');
      return `  <url>\n    <loc>${escape(url(publicBaseUrl, language, path))}</loc>\n${alternates}\n  </url>`;
    }),
  );
  return (
    '<?xml version="1.0" encoding="UTF-8"?>\n' +
    '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:xhtml="http://www.w3.org/1999/xhtml">\n' +
    entries.join('\n') +
    '\n</urlset>\n'
  );
}

function url(publicBaseUrl: string, language: string, path: string): string {
  return `${publicBaseUrl}/${language}${path ? `/${path}` : ''}`;
}

async function everyPage<T>(endpoint: string): Promise<T[]> {
  const all: T[] = [];
  for (let page = 1; page <= MAX_PAGES; page++) {
    const result = await getJson<Paged<T>>(`${endpoint}?page=${page}&pageSize=${PAGE_SIZE}`);
    all.push(...result.items);
    if (!result.hasNext) break;
  }
  return all;
}

async function getJson<T>(address: string): Promise<T> {
  const response = await fetch(address, { headers: { Accept: 'application/json' }, signal: AbortSignal.timeout(20_000) });
  if (!response.ok) throw new Error(`${address} answered ${response.status}`);
  return (await response.json()) as T;
}

function escape(text: string): string {
  return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}
