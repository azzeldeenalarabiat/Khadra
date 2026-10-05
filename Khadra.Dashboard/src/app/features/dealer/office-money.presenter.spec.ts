import { describe, expect, it } from 'vitest';
import { AR } from '../../core/i18n/ar';
import { EN, TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';
import { resolveMessage } from '../../core/i18n/resolve';
import { BookingFinancials, FinancialPayment, FinancialRefund } from '../../core/models/financials.api';
import { Money } from '../../core/models/fleet.api';
import { officeMoney } from './office-money.presenter';

/**
 * The rental office's Financial section (payments Phase 4b), from the office projection of the booking's
 * financial state, against the REAL dictionaries so these read as the lines an office sees.
 */
const en = (key: TranslationKey, params?: MessageParams) => resolveMessage(EN[key], params, 'en-GB', false) ?? key;
// Arabic wraps every value in zero-width direction isolates (resolve.ts); compare the visible words.
const ar = (key: TranslationKey, params?: MessageParams) =>
  (resolveMessage(AR[key], params, 'ar-JO-u-nu-latn', true) ?? key).replace(/[\u2068\u2069]/g, '');
const format = {
  money: (value: Money) => `${value.amount} ${value.currency}`,
  percent: (value: number) => `${value}%`,
  dateTime: (iso: string) => iso.slice(0, 10),
};
const jod = (amount: number): Money => ({ amount, currency: 'JOD' });

const refund = (reason: string, status: string, amount: number): FinancialRefund => ({
  refundId: `r-${reason}`, paymentId: 'p-1', reason, status, amount: jod(amount), feePart: null, bookingPart: jod(amount),
  requestedAt: '2026-09-26T05:14:00Z', sentAt: null, settledAt: null, failedAt: null, disputeTicketId: null,
  providerReference: null, failureCode: null,
});

const payment = (purpose: string, refunds: FinancialRefund[] = []): FinancialPayment => ({
  paymentId: 'p-1', purpose, status: 'Applied', refundProgress: 'None', occurredAt: '2026-09-25T20:56:00Z',
  appliedToBooking: jod(purpose === 'FullPayment' ? 102.75 : 18), amountCharged: null, processingFee: null,
  feeRefundable: null, refunds, createdAt: null, isSandbox: null, providerReference: null, failureCode: null,
  orphanReason: null,
});

function financials(overrides: Partial<BookingFinancials> = {}): BookingFinancials {
  return {
    bookingId: 'b-1', bookingStatus: 'Confirmed', currency: 'JOD', generatedAt: '2026-09-26T10:00:00Z',
    calculatorVersion: 1, needsReview: false,
    summary: {
      rentalSubtotal: jod(90), deliveryFee: jod(12.75), bookingTotal: jod(102.75), requiredDeposit: jod(18),
      securityDeposit: jod(150), paidOnline: jod(18), processingFees: null, chargedOnline: null,
      refunded: jod(0), refundInProgress: jod(0), refundDelayed: jod(0), days: 3, dailyRate: jod(30), depositPercent: 20,
    },
    balance: { state: 'DueAtHandover', amount: jod(84.75), cashRecorded: [] },
    deposit: { state: 'Held', amount: jod(18), windowEndsAt: null, refund: null, decision: null },
    commission: { amount: jod(6), percent: 20, basis: 'OneDay', state: 'Expected' },
    payments: [payment('Deposit')],
    issues: null,
    ...overrides,
  };
}

const lines = (view: ReturnType<typeof officeMoney>) => view.lines.map((line) => [line.k, line.v]);

describe("the office's Financial section", () => {
  it('states a confirmed deposit booking from the server state, in both languages', () => {
    const english = officeMoney(financials(), en, format);
    const arabic = officeMoney(financials(), ar, format);

    expect(lines(english)).toEqual([
      ['Rental · 3 × 30 JOD', '90 JOD'],
      ['Delivery fee (yours)', '12.75 JOD'],
      ['Security deposit (held per car)', '150 JOD'],
      ['Deposit paid by card (20%)', '18 JOD'],
      ['Balance to collect in cash at handover', '84.75 JOD'],
      ['Deposit', 'Held until pickup, then counted towards the rental'],
      ['Platform commission · 20% of one daily rate (frozen on this booking)', '6 JOD · expected'],
      ['Net payout', "Worked out once this booking's outcome is final"],
    ]);
    expect(english.reviewing).toBe(false);
    expect(arabic.lines.map((line) => line.k)).toContain('المبلغ المتبقي نقدًا عند التسليم');
    expect(arabic.lines.find((line) => line.k === 'العربون')?.v).toBe('محتجز حتى الاستلام، ثم يُحتسب من قيمة الإيجار');
  });

  it('shows the office its own dispute share and booking money only, never the customer share or a fee', () => {
    // The KH-NY8AHLNK shape, as the office projection serves it: the dispute refund row is not sent.
    const view = officeMoney(
      financials({
        bookingStatus: 'Cancelled',
        summary: { ...financials().summary, paidOnline: jod(102.75), refunded: jod(0), refundInProgress: jod(84.75) },
        balance: { state: 'NotDue', amount: jod(0), cashRecorded: [] },
        deposit: {
          state: 'DecidedByDispute', amount: jod(18), windowEndsAt: null, refund: null,
          decision: { ticketIds: ['t-1'], decidedAt: '2026-09-26T05:37:00Z', toCustomer: null, toCustomerRefundStatus: null, toOffice: jod(9), chargedToOffice: null, keptByPlatform: null },
        },
        commission: { amount: jod(6), percent: 20, basis: 'OneDay', state: 'Undecided' },
        payments: [payment('FullPayment', [refund('EndedBeforePickup', 'Sent', 84.75)])],
      }),
      en,
      format,
    );

    expect(lines(view)).toEqual([
      ['Rental · 3 × 30 JOD', '90 JOD'],
      ['Delivery fee (yours)', '12.75 JOD'],
      ['Security deposit (held per car)', '150 JOD'],
      ['Paid in full by card', '102.75 JOD'],
      ['Balance to collect in cash at handover', 'Nothing further is due'],
      ['Refund — paid above the deposit', 'Refund of 84.75 JOD to the customer initiated'],
      ['Deposit', 'Decided by a dispute'],
      ['Dispute decision — to you', '9 JOD'],
      ['Platform commission · 20% of one daily rate (frozen on this booking)', '6 JOD · decided when the booking is final, and never more than your money on it'],
      ['Net payout', "Worked out once this booking's outcome is final"],
    ]);
    const text = JSON.stringify(view.lines);
    expect(text).not.toMatch(/fee \d|processing/i);
    expect(text).not.toContain('to the customer:');
    // A booking paid in full is never called a deposit, in Arabic either (owner, 2026-09-25).
    const arabic = officeMoney(financials({ payments: [payment('FullPayment')] }), ar, format);
    expect(arabic.lines[3].k).toBe('مدفوع بالكامل بالبطاقة');
    expect(officeMoney(financials(), ar, format).lines[3].k).toBe('العربون المدفوع بالبطاقة (20%)');
  });

  it('says a booking that earned nothing earned nothing, and shows no payout for it (item 158)', () => {
    const freeCancellation = officeMoney(
      financials({
        bookingStatus: 'Cancelled',
        balance: { state: 'NotDue', amount: jod(0), cashRecorded: [] },
        deposit: { state: 'ReturnedWithPayment', amount: jod(18), windowEndsAt: null, refund: refund('FreeCancellation', 'Settled', 18), decision: null },
        commission: { amount: jod(6), percent: 20, basis: 'OneDay', state: 'NotEarned' },
        payments: [payment('Deposit', [refund('FreeCancellation', 'Settled', 18)])],
      }),
      en,
      format,
    );
    const neverPaid = officeMoney(
      financials({
        bookingStatus: 'Expired',
        summary: { ...financials().summary, paidOnline: jod(0) },
        balance: { state: 'NotDue', amount: jod(0), cashRecorded: [] },
        deposit: { state: 'NotPaid', amount: jod(0), windowEndsAt: null, refund: null, decision: null },
        commission: { amount: jod(6), percent: 20, basis: 'OneDay', state: 'NotApplicable' },
        payments: [],
      }),
      ar,
      format,
    );

    const commission = (view: ReturnType<typeof officeMoney>) => view.lines.find((line) => line.k.startsWith('Platform commission') || line.k.startsWith('عمولة'))?.v;
    expect(commission(freeCancellation)).toBe('Not earned — the payment went back');
    expect(freeCancellation.lines.some((line) => line.k === 'Net payout')).toBe(false);
    expect(freeCancellation.lines.find((line) => line.k === 'Deposit')?.v).toBe('Returned to the customer with the payment');
    expect(commission(neverPaid)).toBe('لا شيء — لم يُدفع الحجز');
    expect(neverPaid.lines.some((line) => line.k === 'صافي التحويل')).toBe(false);
    // A deposit nobody paid is not a line at all.
    expect(neverPaid.lines.some((line) => line.k === 'العربون')).toBe(false);
  });

  it('never says the balance was collected without a record, and shows the cash the office did record', () => {
    const view = officeMoney(
      financials({
        bookingStatus: 'PickedUp',
        balance: { state: 'CashAtHandover', amount: jod(84.75), cashRecorded: [{ handover: 'Pickup', amount: jod(234.75), recordedAt: '2026-10-05T10:00:00Z' }] },
        deposit: { state: 'AppliedToRental', amount: jod(18), windowEndsAt: null, refund: null, decision: null },
      }),
      en,
      format,
    );

    expect(lines(view)).toContainEqual(['Balance that was due in cash at handover', '84.75 JOD']);
    expect(lines(view)).toContainEqual(['Cash recorded at pickup', '234.75 JOD']);
    expect(JSON.stringify(view.lines)).not.toContain('collected');
  });

  it('words a deposit held for a penalty and one awaiting a decision, with the window date', () => {
    const open = officeMoney(
      financials({ deposit: { state: 'HeldForAssessedPenalty', amount: jod(18), windowEndsAt: '2026-09-28T05:14:00Z', refund: null, decision: null } }),
      en,
      format,
    );
    const stuck = officeMoney(
      financials({ deposit: { state: 'HeldUnresolved', amount: jod(18), windowEndsAt: '2026-09-28T05:14:00Z', refund: null, decision: null } }),
      ar,
      format,
    );

    expect(open.lines.find((line) => line.k === 'Deposit')?.v).toBe(
      'Held for a penalty assessed on the customer — a dispute can be opened until 2026-09-28',
    );
    expect(stuck.lines.find((line) => line.k === 'العربون')?.v).toBe(
      'محتجز بانتظار التسوية — قُدِّرت غرامة على العميل ولم يُفتح أي نزاع',
    );
  });

  it('says what the booking came to for the office once the ledger has it, and where it stands', () => {
    const recorded = (state: string, net: number, settlement: { settlementId: string; number: string; paidOn: string } | null = null) =>
      officeMoney(
        financials({
          bookingStatus: 'Completed',
          commission: { amount: jod(6), percent: 20, basis: 'OneDay', state: 'Earned', earned: jod(6) },
          office: {
            state, outcome: 'Rental', officeMoney: jod(18), commission: jod(6), charges: jod(0), net: jod(net),
            lines: [], finalAt: '2026-10-08T10:00:00Z', recordedAt: '2026-10-08T10:11:00Z', settlement,
            payableId: null, holds: null, blocks: null,
          },
        }),
        en,
        format,
      );

    expect(lines(recorded('Due', 12)).slice(-2)).toEqual([
      ['Net payout', 'Khadra owes you 12 JOD'],
      ['Where it stands', 'In your next payout'],
    ]);
    expect(lines(recorded('Blocked', -5)).slice(-2)).toEqual([
      ['Net payout', 'You owe Khadra 5 JOD'],
      ['Where it stands', 'Not due yet: something on this booking is still open'],
    ]);
    expect(lines(recorded('Settled', 12, { settlementId: 's-1', number: 'SET-2026-000001', paidOn: '2026-10-12' })).slice(-2)).toEqual([
      // Paid: owed, not owes.
      ['Net payout', 'Khadra owed you 12 JOD'],
      ['Where it stands', 'Paid under SET-2026-000001 on 2026-10-12'],
    ]);
    // A booking that came to nothing either way shows no payout line (item 158).
    expect(recorded('NothingDue', 0).lines.some((line) => line.k === 'Net payout')).toBe(false);
  });

  it('flags records under review without hiding the figures', () => {
    const view = officeMoney(financials({ needsReview: true }), en, format);

    expect(view.reviewing).toBe(true);
    expect(view.lines.length).toBeGreaterThan(5);
    expect(en('money.reviewing')).toBe('Khadra is reviewing the payments on this booking.');
  });
});
