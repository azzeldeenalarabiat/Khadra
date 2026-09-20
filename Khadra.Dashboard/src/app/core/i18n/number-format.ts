/**
 * How a number becomes digits, with nothing injected.
 *
 * `FormatService` owns the locale and the currency scale and adds the bidi isolation; these are the
 * rules underneath it, split out so a spec can pin them without an injector — the same arrangement
 * as `resolve.ts` under `I18nService`.
 */

/**
 * The value to hand `Intl`, with negative zero taken out.
 *
 * `Intl.NumberFormat` keeps the sign of a value that rounds to zero: `-0` prints "-0.000", and so does
 * `-0.0004` at three places. A money figure that reads "−0" is a different statement from zero — and
 * in Arabic, once bidi has carried the sign to the far end, it reads "JOD 0−". So a negative that
 * prints as zero at `fractionDigits` places becomes zero, and nothing else changes: a real negative
 * keeps its sign, and positives are returned untouched.
 *
 * The threshold follows Intl's own rounding (half away from zero). At three places `-0.0005` still
 * prints "-0.001", so only magnitudes strictly below half a unit in the last place are zeroed.
 */
export function withoutNegativeZero(value: number, fractionDigits: number): number {
  if (!(value < 0 || Object.is(value, -0))) return value;
  return Math.abs(value) < 0.5 * 10 ** -fractionDigits ? 0 : value;
}

/**
 * An amount's digits at the currency's scale — "110.000" — with no code and no isolation.
 *
 * `minorUnits` undefined means the server has not said yet: print at whatever scale the figure
 * arrived at, up to three places, rather than invent a scale for a currency nobody named.
 */
export function formatAmount(
  amount: number,
  localeTag: string,
  minorUnits: number | undefined,
): string {
  const maximumFractionDigits = minorUnits ?? 3;
  return new Intl.NumberFormat(localeTag, {
    minimumFractionDigits: minorUnits ?? 0,
    maximumFractionDigits,
  }).format(withoutNegativeZero(amount, maximumFractionDigits));
}

/** A plain figure, at a fixed number of places when one is given and up to three otherwise. */
export function formatNumber(value: number, localeTag: string, fractionDigits?: number): string {
  const maximumFractionDigits = fractionDigits ?? 3;
  return new Intl.NumberFormat(localeTag, {
    minimumFractionDigits: fractionDigits,
    maximumFractionDigits,
  }).format(withoutNegativeZero(value, maximumFractionDigits));
}

/**
 * "75%". The places are stated rather than left to Intl's default, so the negative-zero guard and the
 * formatter can never disagree about where rounding happens.
 */
/**
 * "25–50%": both bounds with Latin digits and one percent sign, or just the percentage when the
 * bounds are equal. Never a negative zero at either end.
 */
export function formatPercentRange(min: number, max: number, localeTag: string): string {
  if (min === max) return formatPercent(min, localeTag);
  const digits = new Intl.NumberFormat(localeTag, { maximumFractionDigits: 3 }).format(
    withoutNegativeZero(min, 3),
  );
  return `${digits}–${formatPercent(max, localeTag)}`;
}

export function formatPercent(value: number, localeTag: string): string {
  const maximumFractionDigits = 3;
  const digits = new Intl.NumberFormat(localeTag, { maximumFractionDigits }).format(
    withoutNegativeZero(value, maximumFractionDigits),
  );
  return `${digits}%`;
}
