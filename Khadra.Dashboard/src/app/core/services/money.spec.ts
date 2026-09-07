import { describe, expect, it } from 'vitest';
import { roundTo, scaleOf } from './money';

/**
 * The dispute workspace balances three legs against the deposit a booking froze. If it balances at a
 * coarser precision than the deposit is held at, the sum it calls balanced is not, and the sum it
 * calls unbalanced can never be fixed.
 */
describe('scaleOf', () => {
  it('reads the precision off the figure rather than assuming a currency', () => {
    expect(scaleOf(120)).toBe(0);
    expect(scaleOf(2.5)).toBe(1);
    expect(scaleOf(31.25)).toBe(2);
    expect(scaleOf(2.005)).toBe(3);
  });

  it('takes the finest precision among the figures being compared', () => {
    expect(scaleOf(120, 0, 0)).toBe(0);
    expect(scaleOf(120, 59.5, 0.25)).toBe(2);
    expect(scaleOf(2.005, 1, 1.005)).toBe(3);
  });

  it('has an answer for the degenerate cases', () => {
    expect(scaleOf()).toBe(0);
    expect(scaleOf(0)).toBe(0);
    expect(scaleOf(Number.NaN, 1.5)).toBe(1);
  });
});

describe('roundTo', () => {
  it('rounds at the scale it is given', () => {
    expect(roundTo(2.4675, 3)).toBe(2.468);
    expect(roundTo(2.4675, 2)).toBe(2.47);
    expect(roundTo(2.4675, 0)).toBe(2);
  });

  /**
   * The bug this pair exists for: a deposit of 2.005 JOD, seeded whole to the customer.
   *
   * At two places the total allocated read 2.00 against 2.005 held — a permanent remainder of 0.01
   * that inputs stepping by 0.01 could not clear, so Resolve never enabled and the ticket could not
   * be closed at all. At the deposit's own three places it balances exactly, which it does.
   */
  it('balances a deposit held in fils, which two places could not', () => {
    const held = 2.005;
    const legs = [held, 0, 0];
    const places = scaleOf(held, ...legs);

    const allocated = roundTo(
      legs.reduce((total, leg) => total + leg, 0),
      places,
    );
    expect(roundTo(held - allocated, places)).toBe(0);

    // The same figures at the old fixed precision left money stranded.
    const coarse = Math.round(legs.reduce((total, leg) => total + leg, 0) * 100) / 100;
    expect(Math.round((held - coarse) * 100) / 100).not.toBe(0);
  });

  it('balances a three-way split of a fils deposit', () => {
    const held = 59.125;
    const legs = [29.5, 0.125, 29.5];
    const places = scaleOf(held, ...legs);
    const allocated = roundTo(
      legs.reduce((total, leg) => total + leg, 0),
      places,
    );

    expect(allocated).toBe(held);
    expect(roundTo(held - allocated, places)).toBe(0);
  });

  /** A split that genuinely does not add up must still be reported as not adding up. */
  it('still refuses a split that leaves money unallocated', () => {
    const held = 120;
    const legs = [50, 20, 0];
    const places = scaleOf(held, ...legs);
    const allocated = roundTo(
      legs.reduce((total, leg) => total + leg, 0),
      places,
    );

    expect(allocated).toBe(70);
    expect(roundTo(held - allocated, places)).toBe(50);
  });

  it('does not let binary floating point strand a hundredth', () => {
    expect(roundTo(0.1 + 0.2, 2)).toBe(0.3);
    expect(roundTo(1.005, 2)).toBe(1.01);
  });
});
