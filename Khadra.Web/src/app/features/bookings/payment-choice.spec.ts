import { describe, expect, it } from 'vitest';
import { PaymentOption } from '../../core/api/bookings.api';
import { chosenOption } from './payment-choice';

const money = (amount: number) => ({ amount, currency: 'JOD' });
const deposit: PaymentOption = {
  purpose: 'Deposit', selectedPaymentAmount: money(50), processingFee: money(0),
  totalChargedNow: money(50), remainingBalanceAfter: money(200),
};
const full: PaymentOption = {
  purpose: 'FullPayment', selectedPaymentAmount: money(250), processingFee: money(0),
  totalChargedNow: money(250), remainingBalanceAfter: money(0),
};

describe('the chosen way to pay', () => {
  it('starts on the deposit, the minimum that confirms', () => {
    expect(chosenOption([deposit, full], null, undefined)).toBe(deposit);
  });

  it('follows the visitor\'s pick, and its figures are the server\'s own', () => {
    const chosen = chosenOption([deposit, full], 'FullPayment', undefined);
    expect(chosen).toBe(full);
    expect(chosen?.totalChargedNow).toEqual(money(250));
    expect(chosen?.remainingBalanceAfter).toEqual(money(0));
  });

  it('shows a half-finished checkout\'s choice when the visitor comes back', () => {
    expect(chosenOption([deposit, full], null, 'FullPayment')).toBe(full);
  });

  it('never invents an option the server did not offer', () => {
    expect(chosenOption([deposit], 'FullPayment', undefined)).toBe(deposit);
    expect(chosenOption([], 'Deposit', undefined)).toBeNull();
    expect(chosenOption(undefined, null, undefined)).toBeNull();
  });
});
