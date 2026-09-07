import { describe, expect, it } from 'vitest';
import { isAtRisk } from './sla';

/**
 * The one piece of judgement the console still makes about an SLA, so the one place a boundary can
 * be wrong. It replaced `hours < 12`, which was 0.75 of a 48-hour promise expressed as an absolute
 * count — right only while the SLA was 48.
 */
describe('isAtRisk', () => {
  const start = Date.parse('2026-09-01T00:00:00Z');
  const day = 86_400_000;
  const hour = 3_600_000;

  // A 48-hour window, the review SLA today.
  const due = start + 2 * day;

  it('is calm for most of the window', () => {
    expect(isAtRisk(start, due, start)).toBe(false);
    expect(isAtRisk(start, due, start + 24 * hour)).toBe(false);
    expect(isAtRisk(start, due, start + 35 * hour)).toBe(false);
  });

  it('turns at three quarters elapsed, exactly', () => {
    expect(isAtRisk(start, due, start + 36 * hour - 1)).toBe(false);
    expect(isAtRisk(start, due, start + 36 * hour)).toBe(true);
    expect(isAtRisk(start, due, start + 47 * hour)).toBe(true);
  });

  /**
   * Past the deadline is not "at risk" — it is breached, which every caller reports as its own,
   * louder state. Saying warn here would quietly downgrade an overrun to a caution.
   */
  it('stops at the deadline rather than staying amber for ever', () => {
    expect(isAtRisk(start, due, due)).toBe(false);
    expect(isAtRisk(start, due, due + hour)).toBe(false);
    expect(isAtRisk(start, due, due + 30 * day)).toBe(false);
  });

  /** The threshold is a proportion, so it moves with the promise instead of meaning 12 hours. */
  it('scales with the window rather than assuming 48 hours', () => {
    const shortDue = start + 4 * hour;
    expect(isAtRisk(start, shortDue, start + 2 * hour)).toBe(false);
    expect(isAtRisk(start, shortDue, start + 3 * hour)).toBe(true);

    const longDue = start + 20 * day;
    expect(isAtRisk(start, longDue, start + 14 * day)).toBe(false);
    expect(isAtRisk(start, longDue, start + 15 * day)).toBe(true);
  });

  /** A window that never opened cannot be three quarters gone. */
  it('refuses to judge a window with no duration', () => {
    expect(isAtRisk(start, start, start)).toBe(false);
    expect(isAtRisk(due, start, start)).toBe(false);
    expect(isAtRisk(Number.NaN, due, start)).toBe(false);
  });
});
