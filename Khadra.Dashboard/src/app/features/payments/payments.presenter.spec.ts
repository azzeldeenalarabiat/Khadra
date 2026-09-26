import { describe, expect, it } from 'vitest';
import { AR } from '../../core/i18n/ar';
import { EN, TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';
import { resolveMessage } from '../../core/i18n/resolve';
import { EnumFamily, enumKey, spellEnumName } from '../../core/i18n/status-key';
import { Money } from '../../core/models/fleet.api';
import { AdminPayment, AdminPaymentListItem, AdminRefundListItem } from '../../core/models/payments.api';
import { paymentPage } from './payment-detail.presenter';
import { paymentRow, refundRow, refundViewLabel } from './payments.presenter';

/**
 * The administrator's payments list, refunds queue and payment page in words (payments Phase 4b),
 * against the REAL dictionaries. Every figure and status is the server's.
 */
type Tr = (key: TranslationKey, params?: MessageParams) => string;
const en: Tr = (key, params) => resolveMessage(EN[key], params, 'en-GB', false) ?? key;
// Arabic wraps every value in zero-width direction isolates (resolve.ts); compare the visible words.
const ar: Tr = (key, params) => (resolveMessage(AR[key], params, 'ar-JO-u-nu-latn', true) ?? key).replace(/[\u2068\u2069]/g, '');
const labelWith = (t: Tr) => (family: EnumFamily, name: string | null | undefined) => {
  const key = enumKey(family, name ?? '');
  return key ? t(key) : spellEnumName(name ?? '');
};
const format = { money: (value: Money) => `${value.amount} ${value.currency}`, when: (iso: string) => iso.slice(0, 16) };
const jod = (amount: number): Money => ({ amount, currency: 'JOD' });

const listed = (over: Partial<AdminPaymentListItem> = {}): AdminPaymentListItem => ({
  paymentId: '01a0d99c-8f0d-7092-8ec4-4176ed842a69', bookingId: 'b-1', bookingReference: 'KH-49UQ364G',
  dealerId: 'd-1', dealerName: 'Al-Nadeem Rentals', customerId: 'c-1', customerName: 'Rana Sharif',
  purpose: 'Deposit', status: 'Applied', refundProgress: 'None', amountCharged: jod(20), processingFee: jod(0),
  appliedToBooking: jod(20), refundSettled: jod(0), isSandbox: true, providerReference: 'sb_1', failureCode: null,
  orphanReason: null, createdAt: '2026-09-25T20:57:00Z', occurredAt: '2026-09-25T20:57:00Z',
  ...over,
});

const refund = (over: Partial<AdminRefundListItem> = {}): AdminRefundListItem => ({
  refundId: 'r-1', paymentId: 'p-1', bookingId: 'b-1', bookingReference: 'KH-NY8AHLNK', dealerId: 'd-1',
  dealerName: null, customerId: 'c-1', customerName: null, reason: 'DisputeResolution', status: 'Failed',
  amount: jod(9), disputeTicketId: 't-1', requestedAt: '2026-09-26T05:37:00Z', sentAt: null, settledAt: null,
  failedAt: '2026-09-26T05:40:00Z', failureCode: 'card_closed', providerReference: null, isSandbox: true,
  ...over,
});

describe('the payments list', () => {
  it('words each attempt from the server, marks test money, and leaves a refund-free one without a refund word', () => {
    const row = paymentRow(listed(), en, labelWith(en), format);

    expect(row).toMatchObject({
      shortId: '01a0d99c', reference: 'KH-49UQ364G', customer: 'Rana Sharif', dealer: 'Al-Nadeem Rentals',
      purpose: 'Deposit', charged: '20 JOD', fee: '0 JOD', status: 'Applied to the booking', tone: 'ok',
      progress: null, sandbox: true,
    });
  });

  it('shows a partly refunded full payment by the payment verdict, in Arabic', () => {
    const row = paymentRow(listed({ purpose: 'FullPayment', refundProgress: 'Partial' }), ar, labelWith(ar), format);

    expect(row.purpose).toBe('الدفع الكامل');
    expect(row.progress).toBe('استُرد جزئيًا');
  });
});

describe('the refunds queue', () => {
  it('words a refused refund as still owed, with the provider code, and says gone parties are gone', () => {
    const row = refundRow(refund(), en, labelWith(en), format);

    expect(row).toMatchObject({
      reason: 'Refund — dispute decision', status: 'Refused — being sent again', tone: 'bad', amount: '9 JOD',
      code: 'card_closed', customer: 'Customer account closed', disputeTicketId: 't-1',
    });
    expect(row.dealer).not.toBe('');
  });

  it('dates an owed refund by when it was recorded, and a settled one by when it came back', () => {
    expect(refundRow(refund(), en, labelWith(en), format).date).toBe('2026-09-26T05:37');
    expect(
      refundRow(refund({ status: 'Settled', settledAt: '2026-09-27T09:12:00Z', failedAt: null }), en, labelWith(en), format).date,
    ).toBe('2026-09-27T09:12');
  });

  it('names its views, the live queue first, and spells a status it does not know', () => {
    expect(refundViewLabel('live', en, labelWith(en))).toBe('Owed now');
    expect(refundViewLabel('Failed', ar, labelWith(ar))).toBe('المرفوضة');
    expect(refundViewLabel('Reversed', en, labelWith(en))).toBe('Reversed');
  });
});

describe("a payment's page", () => {
  const page = (over: Partial<AdminPayment['payment']> = {}, events: AdminPayment['providerEvents'] = []): AdminPayment => ({
    payment: {
      paymentId: '01a0d99c-49a5-7359-8132-95ede7d147b7', purpose: 'FullPayment', status: 'Applied', refundProgress: 'InProgress',
      occurredAt: '2026-09-25T20:56:00Z', appliedToBooking: jod(102.75), amountCharged: jod(107.25), processingFee: jod(4.5),
      feeRefundable: true,
      refunds: [{
        refundId: 'r-1', paymentId: 'p-1', reason: 'EndedBeforePickup', status: 'Sent', amount: jod(89.25), feePart: jod(4.5),
        bookingPart: jod(84.75), requestedAt: '2026-09-26T05:14:00Z', sentAt: '2026-09-26T05:15:00Z', settledAt: null,
        failedAt: null, disputeTicketId: null, providerReference: 'rf_1', failureCode: null,
      }],
      createdAt: '2026-09-25T20:55:00Z', isSandbox: true, providerReference: 'sb_1', failureCode: null, orphanReason: null,
      ...over,
    },
    booking: { bookingId: 'b-1', reference: 'KH-NY8AHLNK', status: 'Cancelled', dealerId: 'd-1', dealerName: null, customerId: 'c-1', customerName: null },
    providerEvents: events,
  });

  it('states the attempt, its refund with booking money and fee apart, and its events', () => {
    const view = paymentPage(
      page({}, [{ receiptId: 'e-1', providerEventId: 'evt_1', kind: 'Captured', outcome: 'Acted', amount: jod(107.25), receivedAt: '2026-09-25T20:56:00Z', tiedBy: 'Reference' }]),
      en,
      labelWith(en),
      format,
    );

    expect(view.title).toBe('Payment 01a0d99c');
    expect(view.subtitle).toBe('Full payment for KH-NY8AHLNK · 107.25 JOD');
    expect(view.notice).toBeNull();
    expect(view.facts).toContainEqual({ k: 'Processing fee', v: '4.5 JOD · refundable' });
    expect(view.facts).toContainEqual({ k: 'Provider reference', v: 'sb_1', code: true });
    expect(view.refunds[0].split).toBe('Booking money: 84.75 JOD · Processing fee: 4.5 JOD');
    expect(view.refunds[0].dates).toEqual(['Recorded 2026-09-26T05:14', 'Sent 2026-09-26T05:15']);
    expect(view.events[0]).toMatchObject({ kind: 'Captured', outcome: 'Acted on', byReference: true });
    expect(view.bookingId).toBe('b-1');
  });

  it('says why an attempt took nothing, and why a capture goes back whole', () => {
    const failed = paymentPage(page({ status: 'Failed', failureCode: 'card_declined', refunds: [] }), en, labelWith(en), format);
    const orphaned = paymentPage(page({ status: 'Orphaned', orphanReason: 'booking.not_awaiting_payment' }), ar, labelWith(ar), format);

    expect(failed.notice).toEqual({ text: 'This attempt failed and nothing was charged. Provider code: card_declined', tone: 'bad' });
    // The amount it asked for is not what it charged: the page says so beside the notice.
    expect(failed.facts).toContainEqual({ k: 'Requested', v: '107.25 JOD' });
    expect(failed.facts).toContainEqual({ k: 'Processing fee', v: '4.5 JOD' });
    expect(failed.facts.map((fact) => fact.k)).not.toContain('Charged to the card');
    expect(orphaned.notice?.tone).toBe('warn');
    expect(orphaned.notice?.text).toContain('booking.not_awaiting_payment');
  });
});
