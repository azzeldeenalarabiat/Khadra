/**
 * Arithmetic on money the console was handed, at the precision it was handed in.
 *
 * The console is never told how many minor units a currency has, and it must not guess. It used to
 * round at two places while `Money` persists three — JOD is divided into 1000 fils — so a deposit
 * ending in a third decimal could not be balanced: 2.005 held allocated to 2.00, left a 0.01
 * remainder that inputs stepping by 0.01 could never clear, and the Resolve button on the one screen
 * where the platform decides money stayed disabled for the life of the ticket.
 *
 * Reading the scale off the figures themselves keeps the sums at the precision the money is actually
 * held at, in whatever currency, without the front end holding an opinion about any of them.
 */

/** Decimal places in a number as it is written; 0 for an integer. */
export function scaleOf(...values: readonly number[]): number {
  return Math.max(
    0,
    ...values.map((value) => {
      if (!Number.isFinite(value)) return 0;
      const decimals = String(value).split('.')[1];
      // Anything printed in exponent form is far below a minor unit and rounds to nothing anyway.
      return decimals && !decimals.includes('e') ? decimals.length : 0;
    }),
  );
}

/** Rounds at the scale of the money being compared, never at an assumed one. */
export function roundTo(value: number, places: number): number {
  const factor = 10 ** places;
  // The nudge is smaller than any currency's minor unit and only ever pushes a value that binary
  // floating point has left a hair under a boundary (2.675 stored as 2.67499…) back onto it.
  return Math.round((value + Number.EPSILON * Math.sign(value)) * factor) / factor;
}
