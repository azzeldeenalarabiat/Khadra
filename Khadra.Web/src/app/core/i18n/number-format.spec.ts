import { describe, expect, it } from 'vitest';
import {
  formatAmount,
  formatNumber,
  formatPercent,
  formatPercentRange,
  withoutNegativeZero,
} from './number-format';

const EN = 'en-GB';
const AR = 'ar-JO-u-nu-latn';
/** A hyphen-minus or U+2212 — Intl's Arabic minus is a hyphen behind a left-to-right mark. */
const SIGN = /[-−]/;

/**
 * Money that is zero prints as zero.
 *
 * The Dealer Reports screen showed a zero platform commission as "JOD 0−" in Arabic: a sign typed
 * into the template, carried to the far end by bidi. The sign is gone from the templates. These pin
 * the formatter underneath, so a negative zero that reaches it from anywhere else cannot print one.
 */
describe('withoutNegativeZero', () => {
  it('turns negative zero into zero', () => {
    expect(Object.is(withoutNegativeZero(-0, 3), 0)).toBe(true);
  });

  it('zeroes a negative too small to survive rounding at the scale', () => {
    expect(withoutNegativeZero(-0.0004, 3)).toBe(0);
    expect(withoutNegativeZero(-0.004, 2)).toBe(0);
  });

  it('keeps a negative that still rounds to a visible amount', () => {
    expect(withoutNegativeZero(-0.0005, 3)).toBe(-0.0005);
    expect(withoutNegativeZero(-12.5, 3)).toBe(-12.5);
  });

  it('never touches zero or a positive', () => {
    expect(withoutNegativeZero(0, 3)).toBe(0);
    expect(withoutNegativeZero(0.0004, 3)).toBe(0.0004);
  });
});

describe('formatAmount', () => {
  it('prints a zero commission as zero in both languages', () => {
    expect(formatAmount(0, EN, 3)).toBe('0.000');
    expect(formatAmount(-0, EN, 3)).toBe('0.000');
    expect(formatAmount(-0.0004, EN, 3)).toBe('0.000');
    expect(formatAmount(-0, AR, 3)).not.toMatch(SIGN);
    expect(formatAmount(-0.0004, AR, 3)).not.toMatch(SIGN);
  });

  it('rounds half away from zero exactly where Intl does', () => {
    expect(formatAmount(-0.0005, EN, 3)).toBe('-0.001');
  });

  it('pads to the currency scale once the server has named it', () => {
    expect(formatAmount(110, EN, 3)).toBe('110.000');
    expect(formatAmount(1234.5, AR, 3)).toBe('1,234.500');
  });

  it('prints at the scale it arrived at before the scale is known', () => {
    expect(formatAmount(9.5, EN, undefined)).toBe('9.5');
    expect(formatAmount(-0, EN, undefined)).toBe('0');
  });
});

describe('formatNumber and formatPercent', () => {
  it('never print negative zero', () => {
    expect(formatNumber(-0, EN)).toBe('0');
    expect(formatNumber(-0.04, EN, 1)).toBe('0.0');
    expect(formatPercent(-0, EN)).toBe('0%');
    expect(formatPercent(-0.0001, AR)).not.toMatch(SIGN);
  });

  it('keep Latin digits under Arabic', () => {
    expect(formatPercent(75, AR)).toBe('75%');
    expect(formatNumber(1234, AR, 0)).toBe('1,234');
  });
});

/** The dealer non-delivery penalty, "25–50%", as one run with one sign — and never "-0%" at either end. */
describe('formatPercentRange', () => {
  it('joins both bounds with one percent sign', () => {
    expect(formatPercentRange(25, 50, 'en-GB')).toBe('25–50%');
    expect(formatPercentRange(12.5, 33.3333, 'ar-JO-u-nu-latn')).toBe('12.5–33.333%');
  });

  it('prints a single percentage when the bounds agree', () => {
    expect(formatPercentRange(40, 40, 'en-GB')).toBe('40%');
  });

  it('never prints a negative zero', () => {
    expect(formatPercentRange(-0, 10, 'en-GB')).toBe('0–10%');
  });
});
