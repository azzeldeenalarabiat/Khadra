import { describe, expect, it } from 'vitest';
import { AR } from '../../core/i18n/ar';
import { EN, TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';
import { resolveMessage } from '../../core/i18n/resolve';
import { EnumFamily, enumKey, spellEnumName } from '../../core/i18n/status-key';
import { Money } from '../../core/models/fleet.api';
import { AdminPayment, AdminPaymentListItem, AdminRefundListItem, PaymentIncident } from '../../core/models/payments.api';
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
      shortId: 'sb_1', reference: 'KH-49UQ364G', customer: 'Rana Sharif', dealer: 'Al-Nadeem Rentals',
      purpose: 'Deposit', charged: '20 JOD', fee: '0 JOD', status: 'Applied to the booking', tone: 'ok',
      progress: null, sandbox: true,
    });
  });

  it('tells two attempts a minute apart by their provider reference, or by the random tail of the id (F38)', () => {
    const failed = paymentRow(listed({ paymentId: '01a10261-6a4c-7b3e-9f10-1c2d3e4f5a6b', providerReference: null }), en, labelWith(en), format);
    const applied = paymentRow(listed({ paymentId: '01a10261-9b2d-7c4f-8e21-6f5e4d3c2b1a', providerReference: 'sbx_9f2c41a07be3d665' }), en, labelWith(en), format);

    expect(failed.shortId).toBe('1c2d3e4f5a6b');
    expect(applied.shortId).toBe('sbx_9f2c41a07be3d665');
    expect(failed.shortId).not.toBe(applied.shortId);
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

  it('says how often a refund was refused and when it is sent again, and raises it when the server says a person must look', () => {
    const waiting = refundRow(refund({ refusalCount: 2, nextAttemptAt: '2026-09-26T05:43:00Z', needsAPerson: false }), en, labelWith(en), format);
    const repeated = refundRow(refund({ refusalCount: 3, nextAttemptAt: '2026-09-26T05:47:00Z', needsAPerson: true }), ar, labelWith(ar), format);

    expect(waiting.refusals).toBe('Refused 2 times · sent again 2026-09-26T05:43');
    expect(waiting.needsAPerson).toBe(false);
    expect(repeated.refusals).toBe('رُفض 3 مرات · يُرسل مجددًا 2026-09-26T05:47');
    expect(repeated.needsAPerson).toBe(true);
    // One that went through after a refusal keeps its count and has nothing waiting.
    expect(refundRow(refund({ status: 'Sent', refusalCount: 1, nextAttemptAt: null }), en, labelWith(en), format).refusals).toBe('Refused once');
  });

  it('says nothing about refusals for a refund never refused, or from an API that sends no count', () => {
    expect(refundRow(refund({ refusalCount: 0 }), en, labelWith(en), format).refusals).toBeNull();
    const fromAnOlderApi = refundRow(refund(), en, labelWith(en), format);
    expect(fromAnOlderApi.refusals).toBeNull();
    expect(fromAnOlderApi.needsAPerson).toBe(false);
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

    expect(view.title).toBe('Payment sb_1');
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

  // ── A capture notice for money already taken, and its incidents (Wave 4, B1) ──

  const notice = (outcome: string, captureReference: string | null = null): AdminPayment['providerEvents'][number] => ({
    receiptId: `e-${outcome}`,
    providerEventId: `evt_${outcome}`,
    kind: 'Captured',
    outcome,
    amount: jod(107.25),
    receivedAt: '2026-09-25T21:00:00Z',
    tiedBy: 'Payment',
    captureReference,
  });

  const incident = (over: Partial<PaymentIncident> = {}): PaymentIncident => ({
    incidentId: 'i-1',
    kind: 'SecondCapture',
    receiptId: 'e-SecondCapture',
    captureReference: 'cap_2',
    reported: jod(107.25),
    expected: jod(107.25),
    otherPaymentId: null,
    detectedAt: '2026-09-25T21:00:00Z',
    handledAt: null,
    handledBy: null,
    handledNote: null,
    ...over,
  });

  it('colours each notice by what it was: the same capture quiet, an assumed one a warning, an incident an alarm', () => {
    const view = paymentPage(
      page({}, ['Acted', 'Duplicate', 'AssumedDuplicate', 'AmountMismatch', 'SecondCapture', 'OtherAttempt', 'Unmatched', 'Novel'].map((outcome) => notice(outcome))),
      en,
      labelWith(en),
      format,
    );

    expect(view.events.map((event) => [event.outcome, event.tone])).toEqual([
      ['Acted on', 'ok'],
      ['Same capture, said again', 'dim'],
      ['Same amount again — assumed the same capture', 'warn'],
      ['Same capture, another amount — incident', 'bad'],
      ['Another capture — incident', 'bad'],
      ['Capture held by another attempt — incident', 'bad'],
      ['No refund matched', 'bad'],
      // An outcome this build does not know is spelled out, and quiet rather than guessed at.
      ['Novel', 'dim'],
    ]);
  });

  it('shows the capture a notice reported, and nothing for one that carried none', () => {
    const view = paymentPage(page({}, [notice('Duplicate', 'sbxcap_0123'), notice('Acted')]), en, labelWith(en), format);

    expect(view.events.map((event) => event.captureReference)).toEqual(['sbxcap_0123', null]);
  });

  it('states each incident with its figures, and who handled it and how once somebody has', () => {
    const view = paymentPage(
      {
        ...page(),
        incidents: [
          incident({ kind: 'AmountMismatch', reported: jod(110), expected: jod(107.25) }),
          incident({ incidentId: 'i-2', kind: 'CaptureOnAnotherAttempt', otherPaymentId: 'p-9', expected: jod(20) }),
          incident({
            incidentId: 'i-3',
            handledAt: '2026-09-26T09:00:00Z',
            handledBy: 'Omar Deeb',
            handledNote: 'Refunded at the provider, ticket 41.',
          }),
        ],
      },
      en,
      labelWith(en),
      format,
    );

    const [mismatch, onOther, handled] = view.incidents ?? [];
    expect(mismatch).toMatchObject({
      kind: 'Amount contradicts the capture',
      open: true,
      status: 'Open',
      tone: 'bad',
      detected: 'Detected 2026-09-25T21:00',
      figures: ['The provider reported 110 JOD', 'This payment had already taken 107.25 JOD'],
      captureReference: 'cap_2',
      handled: null,
    });
    // Measured against what this attempt ASKED for, and linked to the attempt that holds the capture.
    expect(onOther.figures).toEqual(['The provider reported 107.25 JOD', 'This attempt asked for 20 JOD']);
    expect(onOther.otherPaymentId).toBe('p-9');
    expect(handled).toMatchObject({
      open: false,
      status: 'Handled',
      tone: 'ok',
      handled: 'Marked handled by Omar Deeb · 2026-09-26T09:00',
      note: 'Refunded at the provider, ticket 41.',
    });
    // Two open: said once above everything.
    expect(view.incidentNotice).toBe(
      '2 captures on this payment need checking at the provider: money may have been taken twice, or reported wrongly. Nothing was refunded automatically.',
    );
  });

  it('names nobody when the handler no longer resolves, and says nothing above the page once all are handled', () => {
    const view = paymentPage(
      { ...page(), incidents: [incident({ handledAt: '2026-09-26T09:00:00Z', handledBy: null, handledNote: 'Done.' })] },
      ar,
      labelWith(ar),
      format,
    );

    expect(view.incidents?.[0].handled).toBe('عولجت · 2026-09-26T09:00');
    expect(view.incidents?.[0].kind).toBe('خصم ثانٍ');
    expect(view.incidentNotice).toBeNull();
  });

  it('draws no incidents section when the API sent none, and an empty one when it sent an empty list', () => {
    expect(paymentPage(page(), en, labelWith(en), format).incidents).toBeNull();
    expect(paymentPage({ ...page(), incidents: [] }, en, labelWith(en), format).incidents).toEqual([]);
  });

  it('counts one open incident in every Arabic form the notice needs', () => {
    const open = (count: number) =>
      paymentPage(
        { ...page(), incidents: Array.from({ length: count }, (_, index) => incident({ incidentId: `i-${index}` })) },
        ar,
        labelWith(ar),
        format,
      ).incidentNotice;

    expect(open(1)).toContain('خصم على هذه الدفعة يحتاج');
    expect(open(2)).toContain('خصمان على هذه الدفعة يحتاجان');
    expect(open(3)).toContain('3 خصومات');
    expect(open(11)).toContain('11 خصمًا');
  });
});
