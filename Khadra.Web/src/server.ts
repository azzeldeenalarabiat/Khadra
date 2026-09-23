import {
  AngularNodeAppEngine,
  createNodeRequestHandler,
  isMainModule,
  writeResponseToNodeResponse,
} from '@angular/ssr/node';
import express from 'express';
import { timingSafeEqual } from 'node:crypto';
import { join } from 'node:path';
import { negotiateLanguage } from './app/core/i18n/negotiate';
import { ServerRenderContext } from './app/core/http/server-context';
import { PRIVATE_PAGES } from './app/app.routes.server';
import { carsSitemap, officesSitemap, pagesSitemap, sitemapIndex } from './sitemap';

/**
 * The customer website's renderer. It sits BEHIND the customer BFF, which owns the public host, the
 * session and `/api`; everything the BFF does not claim arrives here to be rendered.
 *
 * Configuration, all from the environment (nothing about a host is written into the build):
 *   KHADRA_API_URL          where the renderer reaches the API directly, e.g. http://khadra:8080
 *   KHADRA_PUBLIC_BASE_URL  the public origin, e.g. https://www.example.jo — canonical URLs, sitemap
 *   KHADRA_EDGE_SECRET      shared with the BFF (BffSecurity:FrontendSharedSecret); proves a request
 *                           came through it, so the client address it names can be believed
 *   PORT                    default 4000
 */
const apiBaseUrl = process.env['KHADRA_API_URL'] ?? 'http://localhost:5112';
const publicBaseUrl = (process.env['KHADRA_PUBLIC_BASE_URL'] ?? 'https://localhost:7244').replace(/\/$/, '');
const edgeSecret = process.env['KHADRA_EDGE_SECRET'] ?? '';

if (process.env['NODE_ENV'] === 'production' && (!process.env['KHADRA_API_URL'] || !process.env['KHADRA_PUBLIC_BASE_URL'])) {
  throw new Error('KHADRA_API_URL and KHADRA_PUBLIC_BASE_URL must be set: the renderer has no host to assume.');
}

const browserDistFolder = join(import.meta.dirname, '../browser');
const app = express();
app.disable('x-powered-by');
const angularApp = new AngularNodeAppEngine();

/** Content-hashed build output (`main-ABCD1234.js`) never changes under its name; everything else may. */
const HASHED = /-[A-Z0-9]{8}\.(?:js|css|woff2?|ttf|png|jpg|svg)$/;

/** Account pages and the forms leading into one: never indexed, never cached. */
const PRIVATE = new RegExp(
  `^/(?:ar|en)/(?:${PRIVATE_PAGES.map((page) => page.replace('/**', '(?:/.*)?')).join('|')})/?$`,
);

/**
 * The visitor's address, but only when the request proves it came through the customer BFF. The BFF
 * writes the address it accepted the connection from as the LAST X-Forwarded-For entry; anything
 * before it was written by someone else. Without the secret the renderer names nobody, and the API
 * sees the renderer — never a value the visitor chose.
 */
function clientAddress(request: express.Request): string | null {
  if (!edgeSecret) return null;
  const presented = Buffer.from(request.header('x-khadra-edge') ?? '');
  const expected = Buffer.from(edgeSecret);
  if (presented.length !== expected.length || !timingSafeEqual(presented, expected)) return null;
  const entries = (request.header('x-forwarded-for') ?? '').split(',').map((entry) => entry.trim()).filter(Boolean);
  return entries.at(-1) ?? null;
}

app.get('/', (request, response) => {
  // The one place the browser's language preference is read (owner, 2026-09-23): clearly English goes
  // to English, everything else to Arabic. 302 and uncached, because the answer depends on the header.
  response.setHeader('Vary', 'Accept-Language');
  response.setHeader('Cache-Control', 'private, no-store');
  response.redirect(302, `/${negotiateLanguage(request.header('accept-language'))}`);
});

app.get('/robots.txt', (_request, response) => {
  response.type('text/plain').setHeader('Cache-Control', 'public, max-age=3600');
  response.send(
    [
      'User-agent: *',
      'Disallow: /api/',
      'Disallow: /bff/',
      ...PRIVATE_PAGES.flatMap((page) => ['ar', 'en'].map((language) => `Disallow: /${language}/${page.replace('/**', '/')}`)),
      '',
      `Sitemap: ${publicBaseUrl}/sitemap.xml`,
      '',
    ].join('\n'),
  );
});

const sitemaps: Record<string, () => Promise<string>> = {
  '/sitemap.xml': () => sitemapIndex(publicBaseUrl),
  '/sitemap-pages.xml': () => pagesSitemap(publicBaseUrl, apiBaseUrl),
  '/sitemap-cars.xml': () => carsSitemap(publicBaseUrl, apiBaseUrl),
  '/sitemap-offices.xml': () => officesSitemap(publicBaseUrl, apiBaseUrl),
};
// Built at most once an hour per file, whatever the request rate: a sitemap walks the whole
// catalogue, and rebuilding it per request would let anyone make the API page every car on demand.
// One build in flight at a time; while it runs, the previous copy keeps being served.
const SITEMAP_TTL_MS = 60 * 60 * 1000;
const built = new Map<string, { xml: string; at: number }>();
const building = new Map<string, Promise<string>>();
function cachedSitemap(path: string, build: () => Promise<string>): Promise<string> {
  const current = built.get(path);
  const fresh = current && Date.now() - current.at < SITEMAP_TTL_MS;
  if (!fresh && !building.has(path)) {
    const pending = build()
      .then((xml) => {
        built.set(path, { xml, at: Date.now() });
        return xml;
      })
      .finally(() => building.delete(path));
    building.set(path, pending);
  }
  return current ? Promise.resolve(current.xml) : building.get(path)!;
}

for (const [path, build] of Object.entries(sitemaps)) {
  app.get(path, (_request, response, next) => {
    cachedSitemap(path, build)
      .then((xml) => {
        response.type('application/xml').setHeader('Cache-Control', 'public, max-age=3600');
        response.send(xml);
      })
      // The API is asleep or failing: say so with a status a crawler retries, never an empty map.
      .catch(() => response.status(503).setHeader('Retry-After', '120').send())
      .catch(next);
  });
}

app.use(
  express.static(browserDistFolder, {
    index: false,
    redirect: false,
    setHeaders: (response, path) => {
      response.setHeader(
        'Cache-Control',
        HASHED.test(path) ? 'public, max-age=31536000, immutable' : 'public, max-age=86400',
      );
    },
  }),
);

// A page address with no language — the links in customer emails (`/verify-email?token=…`,
// `/bookings/{id}`) and the future app links share one host and cannot know the reader's language —
// is sent to the same page in the language the browser prefers, query kept. Anything the router does
// not know then answers its real 404 in that language.
app.get(/^\/(?!ar(?:\/|$)|en(?:\/|$))[^.]*$/, (request, response) => {
  const query = request.originalUrl.includes('?') ? request.originalUrl.slice(request.originalUrl.indexOf('?')) : '';
  response.setHeader('Vary', 'Accept-Language');
  response.setHeader('Cache-Control', 'private, no-store');
  response.redirect(302, `/${negotiateLanguage(request.header('accept-language'))}${request.path}${query}`);
});

app.use((request, response, next) => {
  const context: ServerRenderContext = { apiBaseUrl, publicBaseUrl, clientAddress: clientAddress(request) };
  const isPrivate = PRIVATE.test(request.path);

  angularApp
    .handle(request, context)
    .then(async (rendered) => {
      if (!rendered) return next();
      response.setHeader('Cache-Control', isPrivate ? 'private, no-store' : 'no-cache');
      if (isPrivate) response.setHeader('X-Robots-Tag', 'noindex, nofollow');

      // A page rendered in the browser is served as the empty shell, whose <html> says Arabic. For an
      // English address, say English from the first byte so the page does not open mirrored.
      if (isPrivate && request.path.startsWith('/en/') && rendered.headers.get('content-type')?.includes('text/html')) {
        const html = (await rendered.text()).replace(/<html lang="ar" dir="rtl">/, '<html lang="en" dir="ltr">');
        return writeResponseToNodeResponse(new Response(html, rendered), response);
      }
      return writeResponseToNodeResponse(rendered, response);
    })
    .catch(next);
});

if (isMainModule(import.meta.url) || process.env['pm_id']) {
  const port = process.env['PORT'] || 4000;
  app.listen(port, (error) => {
    if (error) throw error;
    console.log(`Khadra website renderer listening on http://localhost:${port} (API ${apiBaseUrl}, public ${publicBaseUrl})`);
  });
}

export const reqHandler = createNodeRequestHandler(app);
