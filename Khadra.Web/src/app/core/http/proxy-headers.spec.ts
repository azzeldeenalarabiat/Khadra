import { describe, expect, it } from 'vitest';
import { stripProxyHeaders } from './proxy-headers';

/** Pre-launch item 223: Angular is handed no proxy header, so it has none to warn about. */
describe('stripProxyHeaders', () => {
  it('removes Forwarded and every X-Forwarded-* header, and nothing else', () => {
    const headers: Record<string, unknown> = {
      host: 'localhost:4000',
      'x-forwarded-for': '203.0.113.7',
      'x-forwarded-proto': 'https',
      'x-forwarded-host': 'www.example.jo',
      forwarded: 'for=203.0.113.7',
      'x-khadra-edge': 'secret',
      'accept-language': 'ar',
    };

    const removed = stripProxyHeaders(headers);

    expect(removed.sort()).toEqual([
      'forwarded',
      'x-forwarded-for',
      'x-forwarded-host',
      'x-forwarded-proto',
    ]);
    expect(headers).toEqual({
      host: 'localhost:4000',
      'x-khadra-edge': 'secret',
      'accept-language': 'ar',
    });
  });
});
