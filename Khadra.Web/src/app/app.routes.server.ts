import { RenderMode, ServerRoute } from '@angular/ssr';

/**
 * Which pages the server renders.
 *
 * Public pages (home, cars, a car, rental offices, an office) are rendered on the server on every
 * request: their content is live — prices and availability change — so nothing is prerendered at
 * build time, and a crawler reads real HTML.
 *
 * Everything that belongs to an account, and the forms that lead into one, render in the browser
 * only: the renderer never holds a session, so it has nothing true to put in them, and they carry
 * `noindex` both in the page and in `X-Robots-Tag` (see server.ts).
 */
export const PRIVATE_PAGES = [
  'login',
  'register',
  'verify-email',
  'forgot-password',
  'reset-password',
  'book/**',
  'bookings',
  'bookings/**',
  'saved',
  'notifications',
  'profile',
  'profile/**',
] as const;

/** The private pages built so far; each is added here as its page lands. */
const BUILT_PRIVATE_PAGES: readonly (typeof PRIVATE_PAGES)[number][] = [
  'login',
  'register',
  'verify-email',
  'forgot-password',
  'reset-password',
  'profile',
  'profile/**',
  'book/**',
  'bookings',
  'bookings/**',
  'saved',
  'notifications',
];

export const serverRoutes: ServerRoute[] = [
  ...(['ar', 'en'] as const).flatMap((language) =>
    BUILT_PRIVATE_PAGES.map((page): ServerRoute => ({ path: `${language}/${page}`, renderMode: RenderMode.Client })),
  ),
  { path: '**', renderMode: RenderMode.Server },
];
