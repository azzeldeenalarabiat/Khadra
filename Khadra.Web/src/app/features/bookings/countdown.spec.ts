import { describe, expect, it } from 'vitest';
import { clockCountdown, countdownParts, countdownUnits } from './countdown';

const NOW = Date.parse('2026-09-23T10:00:00Z');

describe('countdown', () => {
  it('names the two largest units', () => {
    expect(countdownUnits(countdownParts('2026-09-24T14:30:00Z', NOW)!)).toEqual([
      ['days', 1],
      ['hours', 4],
    ]);
    expect(countdownUnits(countdownParts('2026-09-23T11:59:00Z', NOW)!)).toEqual([
      ['hours', 1],
      ['minutes', 59],
    ]);
    expect(countdownUnits(countdownParts('2026-09-23T12:00:00Z', NOW)!)).toEqual([['hours', 2]]);
  });

  it('never says zero minutes while time remains', () => {
    expect(countdownUnits(countdownParts('2026-09-23T10:00:20Z', NOW)!)).toEqual([['minutes', 1]]);
  });

  it('knows when the time is over, and never goes negative', () => {
    const parts = countdownParts('2026-09-23T09:00:00Z', NOW)!;
    expect(parts.over).toBe(true);
    expect(parts.minutes).toBe(0);
  });

  it('prints a short code lifetime as a clock', () => {
    expect(clockCountdown(countdownParts('2026-09-23T10:14:05Z', NOW)!)).toBe('14:05');
  });

  it('answers nothing for a missing or broken deadline', () => {
    expect(countdownParts(null, NOW)).toBeNull();
    expect(countdownParts('not a date', NOW)).toBeNull();
  });
});
