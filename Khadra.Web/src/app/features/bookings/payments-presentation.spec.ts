import { describe, expect, it } from 'vitest';
import { Money } from '../../core/api/common.api';
import { BookingFinancials, FinancialRefund } from '../../core/api/financials.api';
import { AR } from '../../core/i18n/ar';
import { EN, TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';
import { resolveMessage } from '../../core/i18n/resolve';
import { paymentsView } from './payments-presentation';

/**
 * The booking's "Payments" section in words (payments Phase 4, owner 2026-09-26), against the REAL
 * dictionaries, so these read as the sentences a customer sees in both languages. Every figure comes
 * from the server's financial state; the presenter only chooses sentences.
 */
const en = (key: TranslationKey, params?: MessageParams) => resolveMessage(EN[key], params, 'en-GB', false) ?? key;
// Arabic wraps every value in zero-width direction isolates (resolve.ts); compare the visible words.
const ar = (key: TranslationKey, params?: MessageParams) =>
  (resolveMessage(AR[key], params, 'ar-JO-u-nu-latn', true) ?? key).replace(/[\u2068\u2069]/g, '');
const format = {
  money: (value: Money | null | undefined) => (value ? `${value.amount} ${value.currency}` : '—'),
  dateTime: (iso: string) => iso.slice(0, 10),
};
const jod = (amount: number): Money => ({ amount, currency: 'JOD' });

const refund = (reason: string, status: string, amount: number): FinancialRefund => ({
  refundId: `r-${reason}`, paymentId: 'p-1', reason, status, amount: jod(amount), feePart: jod(0),
  requestedAt: '2026-09-26T10:00:00Z', sentAt: null, settledAt: status === 'Settled' ? '2026-09-27T10:00:00Z' : null,
  failedAt: status === 'Failed' ? '2026-09-26T11:00:00Z' : null, disputeTicketId: null,
});

function state(overrides: Omit<Partial<BookingFinancials>, 'deposit'> & { deposit?: Partial<BookingFinancials['deposit']> } = {}): BookingFinancials {
  const { deposit, ...rest } = overrides;
  return {
    bookingId: 'b-1', bookingStatus: 'Confirmed', currency: 'JOD', generatedAt: '2026-09-26T10:00:00Z',
    calculatorVersion: 1, needsReview: false,
    summary: {
      rentalSubtotal: jod(90), deliveryFee: jod(0), bookingTotal: jod(90), requiredDeposit: jod(18),
      securityDeposit: jod(150), paidOnline: jod(18), processingFees: jod(0), chargedOnline: jod(18),
      refunded: jod(0), refundInProgress: jod(0), refundDelayed: jod(0),
    },
    balance: { state: 'DueAtHandover', amount: jod(72), cashRecorded: [] },
    payments: [{
      paymentId: 'p-1', purpose: 'Deposit', status: 'Applied', refundProgress: 'None', occurredAt: '2026-09-26T09:00:00Z',
      appliedToBooking: jod(18), amountCharged: jod(18), processingFee: jod(0), feeRefundable: true, refunds: [],
    }],
    ...rest,
    deposit: { state: 'Held', amount: jod(18), windowEndsAt: null, refund: null, decision: null, ...deposit },
  };
}

describe('the Payments section', () => {
  it('states what was paid online and what is due to the office at pickup, in both languages', () => {
    const english = paymentsView(state(), en, format);
    const arabic = paymentsView(state(), ar, format);

    expect(english.visible).toBe(true);
    expect(english.totals).toEqual([{ label: 'Paid online', value: '18 JOD' }]);
    expect(english.balance).toEqual({ label: 'Paid to the office at pickup', value: '72 JOD' });
    expect(english.deposit).toBe('Your deposit of 18 JOD is held until you collect the car, when it counts towards the rental.');
    expect(english.payments[0]).toMatchObject({ title: 'Deposit payment', amount: '18 JOD', badge: { label: 'Paid', tone: 'badge--ok' } });
    expect(arabic.totals[0].label).toBe('المدفوع عبر الإنترنت');
    expect(arabic.payments[0].title).toBe('دفعة العربون');
    expect(arabic.deposit).toBe('عربونك البالغ 18 JOD محتجز حتى تستلم السيارة، وعندها يُحتسب من قيمة الإيجار.');
  });

  it('shows nothing before anything is paid', () => {
    const view = paymentsView(state({ payments: [], balance: { state: 'NotYetDue', amount: jod(0), cashRecorded: [] }, deposit: { state: 'NotPaid' } }), en, format);

    expect(view.visible).toBe(false);
  });

  it('shows no card for an answer that carries no deposit state and no payment', () => {
    const sparse = (depositState: string | undefined) =>
      paymentsView(state({ payments: [], balance: { state: 'NotYetDue', amount: jod(0), cashRecorded: [] }, deposit: { state: depositState as string } }), en, format);

    expect(sparse('').visible).toBe(false);
    expect(sparse(undefined).visible).toBe(false);
  });

  it('puts no badge on a refund progress or a refund status it does not know, rather than calling it paid', () => {
    const view = paymentsView(state({
      payments: [{
        paymentId: 'p-1', purpose: 'FullPayment', status: 'Applied', refundProgress: 'SomethingNewer', occurredAt: '2026-09-26T09:00:00Z',
        appliedToBooking: jod(90), amountCharged: jod(90), processingFee: jod(0), feeRefundable: true,
        refunds: [refund('EndedBeforePickup', 'SomethingNewer', 72), refund('DisputeWindowClosed', 'Requested', 18)],
      }],
    }), en, format);

    expect(view.payments[0].badge).toBeNull();
    expect(view.payments[0].refunds.map((line) => line.badge?.label ?? null)).toEqual([null, 'Refund initiated']);
  });

  it('never says the balance was paid at pickup unless the office recorded cash', () => {
    const recorded = paymentsView(state({
      balance: { state: 'CashAtHandover', amount: jod(72), cashRecorded: [{ handover: 'Pickup', amount: jod(222), recordedAt: '2026-10-01T09:00:00Z' }] },
      deposit: { state: 'AppliedToRental' },
    }), en, format);
    const unrecorded = paymentsView(state({ balance: { state: 'CashAtHandover', amount: jod(72), cashRecorded: [] } }), en, format);

    expect(recorded.balance).toEqual({ label: 'Due to the office at pickup', value: '72 JOD' });
    expect(recorded.cash).toEqual([{ label: 'Cash the office recorded at pickup', value: '222 JOD' }]);
    expect(unrecorded.cash).toEqual([]);
    expect(recorded.deposit).toBe('Your deposit of 18 JOD counts towards the rental.');
  });

  it('says a booking paid in full owes nothing more, and an ended one nothing further', () => {
    const full = paymentsView(state({ balance: { state: 'PaidInFull', amount: jod(0), cashRecorded: [] } }), en, format);
    const ended = paymentsView(state({ balance: { state: 'NotDue', amount: jod(0), cashRecorded: [] } }), ar, format);

    expect(full.balance).toBeNull();
    expect(full.balanceText).toBe('You paid the whole booking online, so there is nothing to pay the office.');
    expect(ended.balanceText).toBe('لا يترتب على هذا الحجز أي مبلغ آخر.');
  });

  it('words the held deposit of pre-launch item 164 exactly as the owner decided, in both languages', () => {
    const held = state({ deposit: { state: 'HeldUnresolved' } });

    expect(paymentsView(held, en, format).deposit).toBe(
      'Your deposit remains held because a customer penalty was assessed and no dispute was opened. Final settlement is still pending.',
    );
    expect(paymentsView(held, ar, format).deposit).toBe(
      'لا يزال عربونك محتجزًا لأنّ غرامةً قُدِّرت على العميل ولم يُفتح أيّ نزاع. التسوية النهائية لا تزال معلّقة.',
    );
  });

  it('dates the deposits that wait for the dispute window', () => {
    const windowEndsAt = '2026-09-28T05:14:00Z';

    expect(paymentsView(state({ deposit: { state: 'HeldForAssessedPenalty', windowEndsAt } }), en, format).deposit).toBe(
      'Your deposit of 18 JOD is held because a customer penalty was assessed. A dispute can be opened until 2026-09-28.',
    );
    expect(paymentsView(state({ deposit: { state: 'HeldUntilWindowCloses', windowEndsAt } }), en, format).deposit).toBe(
      'Your deposit of 18 JOD is returned to you after 2026-09-28, unless a dispute is opened before then.',
    );
    expect(paymentsView(state({ deposit: { state: 'InSettlementWindow', windowEndsAt } }), ar, format).deposit).toBe(
      'عربونك البالغ 18 JOD محتجز حتى 2026-09-28 تحسّبًا لفتح نزاع.',
    );
  });

  it('names only the customer\'s own share of a dispute decision', () => {
    const decided = (share: number) =>
      state({ deposit: { state: 'DecidedByDispute', decision: { ticketIds: ['t-1'], decidedAt: '2026-09-26T05:37:00Z', toCustomer: jod(share), toCustomerRefundStatus: 'Requested' } } });

    expect(paymentsView(decided(9), en, format).deposit).toBe('A dispute decided that 9 JOD of your 18 JOD deposit is refunded to you.');
    expect(paymentsView(decided(0), en, format).deposit).toBe('A dispute decided your 18 JOD deposit; none of it is refunded to you.');
    expect(paymentsView(decided(9), ar, format).deposit).toBe('قرّر نزاع أن يُسترد لك 9 JOD من عربونك البالغ 18 JOD.');
  });

  it('lists each payment with its refunds, and reads the refunds as one status', () => {
    const view = paymentsView(state({
      summary: { ...state().summary, paidOnline: jod(90), chargedOnline: jod(94.5), processingFees: jod(4.5), refunded: jod(76.5), refundDelayed: jod(9) },
      payments: [{
        paymentId: 'p-1', purpose: 'FullPayment', status: 'Applied', refundProgress: 'Delayed', occurredAt: '2026-09-26T09:00:00Z',
        appliedToBooking: jod(90), amountCharged: jod(94.5), processingFee: jod(4.5), feeRefundable: true,
        refunds: [refund('EndedBeforePickup', 'Settled', 76.5), refund('DisputeResolution', 'Failed', 9)],
      }],
    }), en, format);

    expect(view.totals.map((line) => line.label)).toEqual(['Paid online', 'Card processing fee', 'Refunded to you', 'Refund delayed — still owed']);
    const payment = view.payments[0];
    expect(payment).toMatchObject({ title: 'Full payment', amount: '94.5 JOD', fee: 'Includes a card processing fee of 4.5 JOD', badge: { label: 'Refund delayed', tone: 'badge--bad' } });
    expect(payment.refunds.map((line) => [line.label, line.amount, line.badge?.label])).toEqual([
      ['Paid above the deposit', '76.5 JOD', 'Refunded'],
      ['Dispute decision', '9 JOD', 'Refund delayed'],
    ]);
  });

  it('shows a capture that could not be applied as being refunded in full', () => {
    const view = paymentsView(state({
      bookingStatus: 'Expired',
      balance: { state: 'NotDue', amount: jod(0), cashRecorded: [] },
      deposit: { state: 'NotPaid' },
      payments: [{
        paymentId: 'p-9', purpose: 'Deposit', status: 'Orphaned', refundProgress: 'InProgress', occurredAt: '2026-09-26T09:00:00Z',
        appliedToBooking: jod(0), amountCharged: jod(18), processingFee: jod(0), feeRefundable: true,
        refunds: [refund('OrphanedCapture', 'Sent', 18)],
      }],
    }), ar, format);

    expect(view.visible).toBe(true);
    expect(view.payments[0].title).toBe('دفعة لم تُحتسب على الحجز');
    expect(view.payments[0].note).toBe('تعذّر احتساب هذه الدفعة على حجزك، لذلك يُسترد لك كامل مبلغها.');
    expect(view.payments[0].refunds[0].label).toBe('دفعة تعذّر استخدامها');
    expect(view.deposit).toBeNull();
  });

  it('says so, neutrally, when the records need a review, and reads nothing out of them about the deposit', () => {
    // The item 165 shape: the rule returns the whole payment, and no refund was ever recorded.
    const reviewing = paymentsView(state({ needsReview: true, balance: { state: 'NotDue', amount: jod(0), cashRecorded: [] }, deposit: { state: 'ReturnedWithPayment' } }), en, format);

    expect(reviewing.reviewing).toBe(true);
    expect(reviewing.deposit).toBeNull();
    expect(reviewing.depositBadge).toBeNull();
    expect(reviewing.totals).toEqual([{ label: 'Paid online', value: '18 JOD' }]);
    expect(reviewing.payments[0].badge?.label).toBe('Paid');
    expect(en('payments.reviewing')).toBe('Khadra is reviewing the payments on this booking.');
    expect(ar('payments.reviewing')).toBe('تراجع خضرا المدفوعات على هذا الحجز.');
  });

  it('words every deposit state it may be sent, in both languages', () => {
    for (const depositState of ['Held', 'AppliedToRental', 'InSettlementWindow', 'UnderDispute', 'SettledWithRental', 'ReturnedWithPayment', 'HeldUntilWindowCloses', 'HeldForAssessedPenalty', 'HeldUnresolved', 'Released']) {
      const input = state({ deposit: { state: depositState, windowEndsAt: '2026-09-28T05:14:00Z' } });
      expect(paymentsView(input, en, format).deposit, depositState).not.toContain('payments.');
      expect(paymentsView(input, ar, format).deposit, depositState).not.toContain('payments.');
    }
  });
});
