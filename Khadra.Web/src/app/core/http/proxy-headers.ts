/**
 * Takes the proxy headers off a request before Angular renders it (pre-launch item 223).
 *
 * The customer BFF adds `X-Forwarded-For` (and YARP its siblings) to every request it hands the renderer. The
 * renderer reads the one it needs itself — the visitor's address, and only behind the edge secret — and takes every
 * URL it prints from `KHADRA_PUBLIC_BASE_URL`, so Angular needs none of them. Angular already ignored them, because
 * nothing told it to trust them, but it logged a warning for each one on every render. Removing them first leaves the
 * render exactly as it was and the log quiet. Trusting the BFF outright is item 140's, once the renderer is on a
 * private network.
 */
export function stripProxyHeaders(headers: Record<string, unknown>): string[] {
  const removed: string[] = [];
  for (const name of Object.keys(headers)) {
    const lower = name.toLowerCase();
    if (lower === 'forwarded' || lower.startsWith('x-forwarded-')) {
      delete headers[name];
      removed.push(lower);
    }
  }
  return removed;
}
