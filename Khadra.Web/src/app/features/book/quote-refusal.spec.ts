import { describe, expect, it } from 'vitest';

import { AppConfig } from '../../core/api/app-config.api';
import { ProblemSnapshot } from '../../core/http/problem';
import { quoteRefusalText, refusedForOpeningHours } from './quote-refusal';

const t = (key: string, params?: Readonly<Record<string, string | number>>) =>
  params ? `${key}:${JSON.stringify(params)}` : key;
const config = { minimumBookingLeadTimeMinutes: 240, maxAdvanceBookingDays: 180, maxRentalDays: 90 } as unknown as AppConfig;
const refused = (code: string, status = 400): ProblemSnapshot => ({ status, code, title: 'Refused.', traceId: 't', errors: null });
const words = (problem: ProblemSnapshot, withConfig: AppConfig | null = config) =>
  quoteRefusalText(problem, t as never, 'en', withConfig, () => 'Mon 6 Oct, 14:00');

describe('quoteRefusalText (E2E F1)', () => {
  it('names the office\'s hours, never only "could not be worked out"', () => {
    expect(words(refused('booking.pickup_outside_opening_hours'))).toBe('book.refusal.booking.pickup_outside_opening_hours');
    expect(words(refused('booking.return_outside_opening_hours'))).toBe('book.refusal.booking.return_outside_opening_hours');
  });

  it('says which date limit was broken, from the published rules', () => {
    expect(words(refused('booking.too_soon'))).toBe('search.tooSoon:{"when":"Mon 6 Oct, 14:00"}');
    expect(words(refused('booking.beyond_horizon'))).toBe('search.tooFar:{"days":180}');
    expect(words(refused('booking.rental_too_long'))).toBe('search.tooLong:{"days":90}');
    expect(words(refused('period.end_before_start'))).toBe('search.returnBeforePickup');
  });

  it('falls back to the plain sentence without the published rules, and for a refusal it does not know', () => {
    expect(words(refused('booking.rental_too_long'), null)).toBe('quote.failed');
    expect(words(refused('booking.unknown_pickup_method'))).toBe('quote.failed');
  });

  it('knows which refusals delivery would answer', () => {
    expect(refusedForOpeningHours(refused('booking.pickup_outside_opening_hours'))).toBe(true);
    expect(refusedForOpeningHours(refused('booking.too_soon'))).toBe(false);
    expect(refusedForOpeningHours(null)).toBe(false);
  });
});
