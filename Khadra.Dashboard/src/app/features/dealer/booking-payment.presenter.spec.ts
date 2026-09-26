import { describe, expect, it } from 'vitest';
import { AR } from '../../core/i18n/ar';
import { EN, TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';
import { resolveMessage } from '../../core/i18n/resolve';
import { refundLines, refundReasonKey, refundStatusKey } from '../../core/i18n/refund-words';
import { confirmedStepKey, depositStillHeld, paidByCardLabel, refundRowKey } from './booking-payment.presenter';

/**
 * A booking paid in full is never worded as a deposit in the dealer console (owner, 2026-09-25).
 *
 * The browser E2E found booking KH-NY8AHLNK, paid 102.750 in full, on a timeline that read "Deposit
 * paid · booking confirmed". Resolved against the REAL dictionaries, so these read as the words a
 * gallery sees, in both languages.
 */
const en = (key: TranslationKey, params?: MessageParams) => resolveMessage(EN[key], params, 'en-GB', false) ?? key;
// Arabic wraps every value in zero-width direction isolates (resolve.ts); compare the visible words.
const ar = (key: TranslationKey, params?: MessageParams) =>
  (resolveMessage(AR[key], params, 'ar-JO-u-nu-latn', true) ?? key).replace(/[\u2068\u2069]/g, '');

describe('the Confirmed step on a booking timeline', () => {
  it('says paid in full when a full payment confirmed the booking', () => {
    expect(en(confirmedStepKey('FullPayment'))).toBe('Paid in full · booking confirmed');
    expect(ar(confirmedStepKey('FullPayment'))).toBe('تم الدفع بالكامل · تأكّد الحجز');
  });

  it('keeps the deposit wording when the deposit confirmed it', () => {
    expect(en(confirmedStepKey('Deposit'))).toBe('Deposit paid · booking confirmed');
    expect(ar(confirmedStepKey('Deposit'))).toBe('تم دفع العربون · تأكّد الحجز');
  });

  it('keeps the deposit wording when the server does not say which payment it was', () => {
    expect(confirmedStepKey(undefined)).toBe('dealerBooking.depositPaidBookingConfirmed');
    expect(confirmedStepKey(null)).toBe('dealerBooking.depositPaidBookingConfirmed');
  });
});

describe('the money line for what the customer paid by card', () => {
  it('names a full payment from the server verdict, not from comparing amounts', () => {
    expect(paidByCardLabel(en, { isPaidInFull: true }, '20%')).toBe('Paid in full by card');
    expect(paidByCardLabel(ar, { isPaidInFull: true }, '20%')).toBe('مدفوع بالكامل بالبطاقة');
  });

  it('names the deposit with its frozen rate otherwise, including on an API too old to say', () => {
    expect(paidByCardLabel(en, { isPaidInFull: false }, '20%')).toBe('Deposit paid by card (20%)');
    expect(paidByCardLabel(en, {}, '20%')).toBe('Deposit paid by card (20%)');
    expect(paidByCardLabel(ar, { isPaidInFull: false }, '20%')).toBe('العربون المدفوع بالبطاقة (20%)');
  });
});

// Phase 3 (owner, 2026-09-26): every refund, with why, how much and where it is, on both consoles.
describe('the refunds a booking lists', () => {
  const refund = (reason: string, status: string, amount = 72) => ({
    refundId: `r-${reason}`, paymentId: 'p-1', reason, status, amount: { amount, currency: 'JOD' },
    requestedAt: '2026-09-26T10:00:00Z', sentAt: null, settledAt: null, failedAt: null, disputeTicketId: null,
  });
  const jod = (value: { amount: number; currency: string }) => `${value.currency} ${value.amount}`;

  it('words each refund by its reason and where it is, in both languages', () => {
    const lines = refundLines(en, [refund('EndedBeforePickup', 'Settled'), refund('DisputeWindowClosed', 'Sent', 18)], jod);
    expect(lines).toEqual([
      { k: 'Refund — paid above the deposit', v: 'JOD 72 refunded to the customer' },
      { k: 'Deposit returned — dispute window closed', v: 'Refund of JOD 18 to the customer initiated' },
    ]);

    const arabic = refundLines(ar, [refund('PlatformCancellation', 'Failed')], jod);
    expect(arabic[0].k).toBe('استرداد — ألغته خضرا');
    expect(arabic[0].v).toContain('ما زال مستحقًا');
  });

  it('never leaves a reason unworded: one a newer server adds reads as a plain refund', () => {
    expect(en(refundReasonKey('SomethingNew'))).toBe('Refund');
    expect(refundStatusKey({ status: 'Requested' })).toBe('booking.refundInitiatedTo');
    expect(refundStatusKey({ status: 'Sent' })).toBe('booking.refundInitiatedTo');
  });

  it('says the deposit is held only while nothing has returned or decided it', () => {
    expect(depositStillHeld({ depositPaid: true, refunds: [] })).toBe(true);
    // The money above the deposit going back leaves the deposit itself held.
    expect(depositStillHeld({ depositPaid: true, refunds: [refund('EndedBeforePickup', 'Settled')] })).toBe(true);
    expect(depositStillHeld({ depositPaid: true, refunds: [refund('DisputeWindowClosed', 'Sent')] })).toBe(false);
    expect(depositStillHeld({ depositPaid: true, refunds: [refund('PlatformCancellation', 'Sent')] })).toBe(false);
    expect(depositStillHeld({ depositPaid: true, refunds: [refund('DisputeResolution', 'Settled')] })).toBe(false);
    expect(depositStillHeld({ depositPaid: false, refunds: [] })).toBe(false);
  });

  it('tells the administrator a platform cancellation returns the whole payment, never that nothing is refunded', () => {
    expect(en('adminBooking.cancelRefundsAmount', { amount: 'JOD 94.5' })).toContain('the deposit included');
    expect(ar('adminBooking.cancelRefundsAmount', { amount: 'JOD 94.5' })).toContain('بما فيه العربون');
  });
});

describe('the refund row a free cancellation leaves', () => {
  it('names the payment when the booking was paid in full, the deposit otherwise', () => {
    expect(en(refundRowKey({ isPaidInFull: true }))).toBe('Payment');
    expect(ar(refundRowKey({ isPaidInFull: true }))).toBe('الدفعة');
    expect(en(refundRowKey({ isPaidInFull: false }))).toBe('Deposit');
    expect(en(refundRowKey({}))).toBe('Deposit');
  });

  it('reads the same way on the administrator booking page', () => {
    expect(en('adminBooking.paymentRefund')).toBe('Payment refund');
    expect(AR['adminBooking.paymentRefund']).not.toContain('العربون');
  });
});

describe('the approval waits for a payment, whichever the customer chooses', () => {
  it('neither the step nor the activity entry assumes the deposit', () => {
    expect(en('dealerBooking.approvedAwaitingPayment')).toBe('Approved · awaiting payment');
    expect(ar('dealerBooking.approvedAwaitingPayment')).toBe('مقبول · بانتظار الدفع');
    expect(en('dealerBooking.paymentReceived')).toBe('Payment received');
    expect(en('dealerActivity.approvedAwaitingPayment')).toBe('Approved — awaiting payment');
    expect(AR['dealerActivity.approvedAwaitingPayment']).not.toContain('العربون');
  });
});

describe('the sentences that describe where money goes', () => {
  // A full payment charges the whole booking by card, delivery fee included, and leaves no cash.
  const keys = ['dealerReports.payoutsAreNotLive', 'dealerSettings.notLiveYetPayouts', 'dealerDelivery.chargedToTheCustomer'] as const;

  it('never say the card payment is a deposit, in either language', () => {
    for (const key of keys) {
      expect(EN[key]).not.toMatch(/card deposit/i);
      expect(AR[key]).not.toContain('عربون البطاقة');
    }
  });

  it('say the delivery fee is collected in cash only when the booking was not paid in full', () => {
    expect(EN['dealerDelivery.chargedToTheCustomer']).toContain('Unless the customer paid the whole booking online');
    expect(AR['dealerDelivery.chargedToTheCustomer']).toContain('ما لم يدفع العميل قيمة الحجز كاملة');
  });
});

describe('the wording that cannot know which payment it was', () => {
  it('the notification bell speaks of a payment, not a deposit', () => {
    expect(en('notifications.customerPaid', { what: 'KH-NY8AHLNK' })).toBe('A customer paid for KH-NY8AHLNK');
    expect(EN['notifications.customerPaid']).not.toMatch(/deposit/i);
    expect(AR['notifications.customerPaid']).not.toContain('العربون');
  });

  it('the activity feed speaks of a payment, not a deposit', () => {
    expect(en('dealerActivity.paymentReceivedBookingConfirmed')).toBe('Payment received — booking confirmed');
    expect(AR['dealerActivity.paymentReceivedBookingConfirmed']).not.toContain('العربون');
  });
});
