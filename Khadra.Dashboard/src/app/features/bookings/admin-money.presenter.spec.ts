import { describe, expect, it } from 'vitest';
import { AR } from '../../core/i18n/ar';
import { EN, TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';
import { resolveMessage } from '../../core/i18n/resolve';
import { EnumFamily, enumKey, spellEnumName } from '../../core/i18n/status-key';
import { BookingFinancials, FinancialPayment, FinancialRefund } from '../../core/models/financials.api';
import { Money } from '../../core/models/fleet.api';
import { adminMoney } from './admin-money.presenter';

/**
 * The administrator's Money section (payments Phase 4b), from the ADMIN projection, against the REAL
 * dictionaries: every share, every attempt, every refund's fee apart, and the records' contradictions.
 */
type Tr = (key: TranslationKey, params?: MessageParams) => string;
const en: Tr = (key, params) => resolveMessage(EN[key], params, 'en-GB', false) ?? key;
// Arabic wraps every value in zero-width direction isolates (resolve.ts); compare the visible words.
const ar: Tr = (key, params) => (resolveMessage(AR[key], params, 'ar-JO-u-nu-latn', true) ?? key).replace(/[\u2068\u2069]/g, '');
/** `I18nService.enumLabel`, over a dictionary. */
const labelWith = (t: Tr) => (family: EnumFamily, name: string | null | undefined) => {
  const key = enumKey(family, name ?? '');
  return key ? t(key) : spellEnumName(name ?? '');
};
const format = {
  money: (value: Money) => `${value.amount} ${value.currency}`,
  percent: (value: number) => `${value}%`,
  dateTime: (iso: string) => iso.slice(0, 10),
};
const jod = (amount: number): Money => ({ amount, currency: 'JOD' });

const refund = (reason: string, status: string, amount: number, fee = 0, failureCode: string | null = null): FinancialRefund => ({
  refundId: `r-${reason}`, paymentId: 'p-1', reason, status, amount: jod(amount), feePart: jod(fee), bookingPart: jod(amount - fee),
  requestedAt: '2026-09-26T05:14:00Z', sentAt: null, settledAt: null, failedAt: null, disputeTicketId: null,
  providerReference: 'rf_1', failureCode,
});

const applied = (refunds: FinancialRefund[]): FinancialPayment => ({
  paymentId: 'p-1', purpose: 'FullPayment', status: 'Applied', refundProgress: 'InProgress', occurredAt: '2026-09-25T20:56:00Z',
  appliedToBooking: jod(102.75), amountCharged: jod(107.25), processingFee: jod(4.5), feeRefundable: true, refunds,
  createdAt: '2026-09-25T20:55:00Z', isSandbox: true, providerReference: 'sb_1', failureCode: null, orphanReason: null,
});

const declined: FinancialPayment = {
  paymentId: 'p-0', purpose: 'Deposit', status: 'Failed', refundProgress: 'None', occurredAt: '2026-09-25T20:50:00Z',
  appliedToBooking: jod(0), amountCharged: jod(18), processingFee: jod(0), feeRefundable: true, refunds: [],
  createdAt: '2026-09-25T20:49:00Z', isSandbox: true, providerReference: 'sb_0', failureCode: 'card_declined', orphanReason: null,
};

function financials(overrides: Partial<BookingFinancials> = {}): BookingFinancials {
  return {
    bookingId: 'b-1', bookingStatus: 'Cancelled', currency: 'JOD', generatedAt: '2026-09-26T10:00:00Z',
    calculatorVersion: 1, needsReview: false,
    summary: {
      rentalSubtotal: jod(90), deliveryFee: jod(12.75), bookingTotal: jod(102.75), requiredDeposit: jod(18),
      securityDeposit: jod(150), paidOnline: jod(102.75), processingFees: jod(4.5), chargedOnline: jod(107.25),
      refunded: jod(0), refundInProgress: jod(98.25), refundDelayed: jod(0), days: 3, dailyRate: jod(30), depositPercent: 20,
    },
    balance: { state: 'NotDue', amount: jod(0), cashRecorded: [] },
    deposit: {
      state: 'DecidedByDispute', amount: jod(18), windowEndsAt: null, refund: null,
      decision: { ticketIds: ['t-1'], decidedAt: '2026-09-26T05:37:00Z', toCustomer: jod(9), toCustomerRefundStatus: 'Sent', toOffice: jod(9), chargedToOffice: null, keptByPlatform: jod(0) },
    },
    commission: { amount: jod(6), percent: 20, basis: 'OneDay', state: 'Undecided' },
    payments: [declined, applied([refund('EndedBeforePickup', 'Sent', 89.25, 4.5), refund('DisputeResolution', 'Failed', 9, 0, 'card_closed')])],
    issues: null,
    ...overrides,
  };
}

describe("the administrator's Money section", () => {
  it('shows every figure and every share of the dispute, in English', () => {
    const money = adminMoney(financials(), 'Delivery', en, labelWith(en), format);

    expect(money.lines.map((line) => [line.k, line.v])).toEqual([
      ['Daily rate × 3 days', '30 JOD'],
      ['Rental total', '90 JOD'],
      ['Delivery fee', '12.75 JOD'],
      ['Total price', '102.75 JOD'],
      ['Deposit (20%)', '18 JOD'],
      ['Paid online (booking money)', '102.75 JOD'],
      ['Card processing fees', '4.5 JOD'],
      ['Charged to the card', '107.25 JOD'],
      ['Refund in progress', '98.25 JOD'],
      ['Balance to collect in cash at handover', 'Nothing further is due'],
      ['Deposit', 'Decided by a dispute'],
      ['Dispute decision — to the customer', '9 JOD'],
      ['Dispute decision — to the office', '9 JOD'],
      ['Dispute decision — kept by Khadra', '0 JOD'],
      ['Security deposit', '150 JOD'],
      ['Platform commission (20% of one daily rate)', '6 JOD · not decided yet'],
    ]);
  });

  it('lists every attempt — the one that took no money included — with its figures and codes', () => {
    const [failed, paid] = adminMoney(financials(), 'Delivery', en, labelWith(en), format).payments;

    expect(failed).toMatchObject({
      paymentId: 'p-0', title: 'Deposit', status: 'Failed — nothing charged', tone: 'bad', progress: null,
      code: 'card_declined', sandbox: true,
      // It took nothing, so its amount is what the checkout asked for, never "charged".
      figures: '18 JOD requested',
    });
    expect(paid).toMatchObject({
      title: 'Full payment', status: 'Applied to the booking', tone: 'ok', progress: 'Refund in progress',
      figures: '107.25 JOD charged · fee 4.5 JOD · 102.75 JOD applied',
    });
    expect(paid.refunds).toEqual([
      {
        k: 'Refund — paid above the deposit',
        v: 'Refund of 89.25 JOD to the customer initiated',
        split: '84.75 JOD booking money · 4.5 JOD fee',
        code: null,
      },
      {
        k: 'Refund — dispute decision',
        v: 'Refund of 9 JOD to the customer delayed — still owed, retrying',
        split: null,
        code: 'card_closed',
      },
    ]);
  });

  it('words the contradictions in the records and flags the booking, in Arabic', () => {
    const money = adminMoney(
      financials({ needsReview: true, issues: ['EndingRefundMissing', 'SomethingNewer'] }),
      'SelfPickup',
      ar,
      labelWith(ar),
      format,
    );

    expect(money.reviewing).toBe(true);
    expect(money.issues).toEqual(['لم يُسجَّل استرداد يستحقه انتهاء هذا الحجز', 'Something newer']);
    // Self-pickup: no delivery line, whatever the frozen fee.
    expect(money.lines.map((line) => line.k)).not.toContain('رسوم التوصيل');
    expect(money.payments[1].status).toBe('احتُسبت على الحجز');
  });
});
