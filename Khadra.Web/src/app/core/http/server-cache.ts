/**
 * The renderer's only cache: a few public, slow-changing reads, kept for a minute in the server
 * process so every page render does not re-ask the API for the list of cities.
 *
 * Deliberately tiny and deliberately exact about what it holds. Availability, prices and quotes are
 * NEVER cached (they are `no-store` at the API for a reason), nothing belonging to an account can
 * reach it (the renderer never has a session), and the key includes the language because the API
 * words vocabulary per `Accept-Language`.
 */
const CACHEABLE_PATHS = new Set(['/api/v1/app-config', '/api/v1/cities', '/api/v1/car-types']);

export const SERVER_CACHE_TTL_MS = 60_000;

interface Entry {
  readonly storedAt: number;
  readonly body: unknown;
}

const entries = new Map<string, Entry>();

export function serverCacheKey(url: string, language: string): string | null {
  const [path, query] = url.split('?', 2);
  if (query !== undefined || !path || !CACHEABLE_PATHS.has(path)) return null;
  return `${language}|${path}`;
}

export function readServerCache(key: string, now = Date.now()): unknown | undefined {
  const entry = entries.get(key);
  if (!entry) return undefined;
  if (now - entry.storedAt > SERVER_CACHE_TTL_MS) {
    entries.delete(key);
    return undefined;
  }
  return entry.body;
}

export function writeServerCache(key: string, body: unknown, now = Date.now()): void {
  entries.set(key, { storedAt: now, body });
}

export function clearServerCache(): void {
  entries.clear();
}
