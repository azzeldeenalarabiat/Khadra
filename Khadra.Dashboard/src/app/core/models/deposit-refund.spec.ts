import { describe, expect, it } from 'vitest';
import { EN } from '../i18n/en';
import { AR } from '../i18n/ar';
import { DepositRefund, depositRefundKey } from './bookings.api';

const refund = (status: string): DepositRefund => ({
  status,
  amount: { amount: 40, currency: 'JOD' },
  requestedAt: '2026-09-24T00:00:00Z',
  sentAt: null,
  settledAt: null,
  failedAt: null,
});

// The free cancellation's refund (owner, 2026-09-24) as the dealer and the admin see it: the same
// three stages the customer does, and never "held pending settlement".
describe('depositRefundKey', () => {
  it('reads requested and sent as initiated, settled as refunded, and failed as delayed', () => {
    expect(depositRefundKey(refund('Requested'))).toBe('booking.depositRefundInitiated');
    expect(depositRefundKey(refund('Sent'))).toBe('booking.depositRefundInitiated');
    expect(depositRefundKey(refund('Settled'))).toBe('booking.depositRefunded');
    expect(depositRefundKey(refund('Failed'))).toBe('booking.depositRefundDelayed');
  });

  it('has words for every stage in both languages', () => {
    for (const status of ['Requested', 'Settled', 'Failed']) {
      const key = depositRefundKey(refund(status));
      expect(EN, key).toHaveProperty([key]);
      expect(AR, key).toHaveProperty([key]);
    }
  });
});
