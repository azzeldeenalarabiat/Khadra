import { describe, expect, it } from 'vitest';
import { AR } from '../../core/i18n/ar';
import { EN, TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';
import { resolveMessage } from '../../core/i18n/resolve';
import { EnumFamily, enumKey } from '../../core/i18n/status-key';
import { Money } from '../../core/models/fleet.api';
import { OfficeBalance, OfficePayable, OfficeSettlement, PayableHold } from '../../core/models/payables.api';
import {
  PayoutFormat,
  PayoutWords,
  balanceRow,
  currentAmountOf,
  financeGroup,
  holdRow,
  lineRow,
  movedText,
  netText,
  officeCard,
  payableRow,
  settledText,
  settlementLineRow,
  settlementRow,
} from './payouts.presenter';

/**
 * The office payables ledger in the consoles' words (payments Phase 8), against the REAL dictionaries, so these read
 * as the lines an administrator and an office see: the sign of a net said in words, never a minus typed before a
 * figure, and what each reader may not see absent.
 */
const words = (lang: 'en' | 'ar'): PayoutWords => {
  const dictionary = lang === 'en' ? EN : AR;
  const t = (key: TranslationKey, params?: MessageParams) =>
    (resolveMessage(dictionary[key], params, lang === 'en' ? 'en-GB' : 'ar-JO-u-nu-latn', lang === 'ar') ?? key).replace(/[⁨⁩]/g, '');
  const label = (family: EnumFamily, name: string | null | undefined) => {
    const key = name ? enumKey(family, name) : null;
    return key ? t(key) : (name ?? '');
  };
  return { t, label };
};
const format: PayoutFormat = {
  money: (value: Money) => `${value.amount} ${value.currency}`,
  dateTime: (iso: string) => iso.slice(0, 16),
  day: (isoDay: string) => isoDay,
};
const jod = (amount: number): Money => ({ amount, currency: 'JOD' });

const payable = (overrides: Partial<OfficePayable> = {}): OfficePayable => ({
  payableId: 'p-1', bookingId: 'b-1', bookingReference: 'KH-ABCD1234', dealerId: 'd-1', dealerName: 'Petra Wheels',
  outcome: 'Rental', state: 'Due', officeMoney: jod(18), commission: jod(6), officeCharges: jod(0), net: jod(12),
  finalAt: '2026-10-08T10:00:00Z', recordedAt: '2026-10-08T10:11:00Z',
  lines: [{ kind: 'RentalRevenue', amount: jod(18), ticketId: null }, { kind: 'Commission', amount: jod(6), ticketId: null }],
  settlement: null, isTest: true, calculatorVersion: 2, holds: [], blocks: [],
  ...overrides,
});

describe('who owes whom', () => {
  it('says the sign of a net in words, from either side, and never prints a minus', () => {
    expect(netText(jod(12), 'admin', words('en'), format)).toEqual({ text: 'Khadra owes the office 12 JOD', tone: 'ok' });
    expect(netText(jod(-5), 'admin', words('en'), format)).toEqual({ text: 'The office owes Khadra 5 JOD', tone: 'warn' });
    expect(netText(jod(-5), 'office', words('en'), format).text).toBe('You owe Khadra 5 JOD');
    expect(netText(jod(0), 'office', words('en'), format)).toEqual({ text: 'Nothing either way', tone: 'dim' });
    expect(netText(jod(12), 'office', words('ar'), format).text).toBe('تدين لك خضرا بمبلغ 12 JOD');
    expect(netText(jod(-5), 'admin', words('en'), format).text).not.toContain('-');
  });

  it("puts the subtraction in a line's label, never a sign before its figure", () => {
    expect(lineRow({ kind: 'Commission', amount: jod(6), ticketId: null }, words('en'), format))
      .toEqual({ label: "Less Khadra's commission", amount: '6 JOD', towardsOffice: false });
    expect(lineRow({ kind: 'PenaltyKept', amount: jod(18), ticketId: null }, words('en'), format))
      .toEqual({ label: "Customer's penalty, kept from the deposit", amount: '18 JOD', towardsOffice: true });
    expect(lineRow({ kind: 'DisputeCharge', amount: jod(9), ticketId: 't-1' }, words('ar'), format).label)
      .toBe('يُخصم: مبلغ حمّله قرار نزاع على المكتب');
  });
});

describe('an office balance', () => {
  const balance = (overrides: Partial<OfficeBalance> = {}): OfficeBalance => ({
    dealerId: 'd-1', dealerName: 'Petra Wheels', currency: 'JOD', provider: 'SANDBOX', isTest: true,
    dueCount: 2, due: jod(-3), notYetDueCount: 1, notYetDue: jod(7),
    lastSettlement: { settlementId: 's-1', number: 'TEST-SET-2026-000001', direction: 'Payout', amount: jod(40), paidOn: '2026-10-01' },
    ...overrides,
  });

  it('can be settled whichever way it runs, and sends back the figure it showed', () => {
    const row = balanceRow(balance(), 'admin', words('en'), format);

    expect(row.due).toBe('The office owes Khadra 3 JOD');
    expect(row.dueAmount).toBe(-3);
    expect(row.canSettle).toBe(true);
    expect(row.notYetDue).toBe('1 booking not due yet: Khadra owes the office 7 JOD');
    expect(row.lastSettlement).toBe('TEST-SET-2026-000001 · Paid to the office · on 2026-10-01');
  });

  it('says held back and not due yet apart, each netted on its own, so they never cancel out (F56 a)', () => {
    const split = balanceRow(
      balance({ notYetDueCount: 2, notYetDue: jod(0), heldCount: 1, held: jod(12), blockedCount: 1, blocked: jod(-12) }),
      'admin',
      words('en'),
      format,
    );
    const arabic = balanceRow(
      balance({ notYetDueCount: 1, notYetDue: jod(12), heldCount: 1, held: jod(12), blockedCount: 0, blocked: jod(0) }),
      'office',
      words('ar'),
      format,
    );

    expect(split.heldBack).toBe('1 booking held back: Khadra owes the office 12 JOD');
    expect(split.notYetDue).toBe('1 booking not due yet: The office owes Khadra 12 JOD');
    expect(arabic.heldBack).toBe('حجز واحد معلّق: تدين لك خضرا بمبلغ 12 JOD');
    expect(arabic.notYetDue).toBeNull();
  });

  it('falls back to the one figure from an API that sends only the total', () => {
    const row = balanceRow(balance(), 'admin', words('en'), format);

    expect(row.heldBack).toBeNull();
    expect(row.notYetDue).toBe('1 booking not due yet: Khadra owes the office 7 JOD');
  });

  it('has nothing to settle when nothing is due, and the office never gets the kind of money', () => {
    expect(balanceRow(balance({ dueCount: 0, due: jod(0) }), 'admin', words('en'), format)).toMatchObject({ canSettle: false, due: 'Nothing due' });
    expect(balanceRow(balance({ provider: null, isTest: null }), 'office', words('en'), format).canSettle).toBe(false);
  });
});

describe('a payable', () => {
  it('may be held while open and released only when an administrator held it', () => {
    const open = payableRow(payable(), 'admin', words('en'), format);
    const held = payableRow(
      payable({ state: 'OnHold', holds: [{ holdId: 'h-1', bookingId: 'b-1', bookingReference: 'KH-ABCD1234', payableId: 'p-1', reason: 'Manual', detail: 'Bank details', openedAt: '2026-10-08T12:00:00Z', openedBy: 'Rana' }] }),
      'admin',
      words('en'),
      format,
    );
    const settled = payableRow(payable({ state: 'Settled', settlement: { settlementId: 's-1', number: 'SET-2026-000001', paidOn: '2026-10-09' } }), 'admin', words('en'), format);

    expect([open.canHold, open.canRelease]).toEqual([true, false]);
    expect([held.canHold, held.canRelease]).toEqual([false, true]);
    expect(held.holds[0].reason).toBe('Held by an administrator');
    expect([settled.canHold, settled.canRelease]).toEqual([false, false]);
    expect(settled.settlement).toEqual({ id: 's-1', number: 'SET-2026-000001', day: '2026-10-09' });
  });

  it('is never offered a hold when it moves no money (F56 c), even while a refund still blocks it', () => {
    const nothingDue = payableRow(payable({ state: 'NothingDue', net: jod(0), lines: [] }), 'admin', words('en'), format);
    const blockedZero = payableRow(payable({ state: 'Blocked', net: jod(0), lines: [] }), 'admin', words('en'), format);

    expect(nothingDue.canHold).toBe(false);
    expect(blockedZero.canHold).toBe(false);
    expect(nothingDue.state).toBe('Nothing due');
  });

  it("words a hold's detail by who wrote it (pre-launch item 219)", () => {
    const hold = (reason: string, detail: string | null): PayableHold => ({
      holdId: 'h-1', bookingId: 'b-1', bookingReference: 'KH-95JGHJQZ', payableId: null, reason, detail,
      openedAt: '2026-09-30T03:00:00Z', openedBy: null,
    });

    // The pass's issue codes, worded as a booking's Money section words them, one per line, in both languages.
    const review = hold('NeedsReview', 'EndingRefundMissing, RefundsConflict');
    expect(holdRow(review, words('en'), format).detail).toEqual({
      kind: 'worded',
      lines: ["A refund this booking's ending owes was never recorded", 'Refunds were recorded that cannot both apply'],
    });
    expect(holdRow(review, words('ar'), format).detail?.lines[0]).toBe('لم يُسجَّل استرداد يستحقه انتهاء هذا الحجز');
    // A code this build has no word for is still shown, never dropped.
    expect(holdRow(hold('NeedsReview', 'SomethingNewer'), words('en'), format).detail?.lines).toEqual(['SomethingNewer']);

    // An administrator's reason as typed; what the server composed, English and figures, as it is.
    expect(holdRow(hold('Manual', 'Waiting for the office to confirm'), words('ar'), format).detail).toEqual({
      kind: 'typed',
      lines: ['Waiting for the office to confirm'],
    });
    expect(holdRow(hold('Contradicted', 'the net is now 10.000, recorded 12.000'), words('ar'), format).detail).toEqual({
      kind: 'server',
      lines: ['the net is now 10.000, recorded 12.000'],
    });
    expect(holdRow(hold('NeedsReview', '  '), words('en'), format).detail).toBeNull();
  });

  it('gives the office no action at all', () => {
    const row = payableRow(payable({ holds: null, blocks: null, isTest: null }), 'office', words('en'), format);

    expect([row.canHold, row.canRelease, row.isTest]).toEqual([false, false, false]);
    expect(row.state).toBe('Due');
    expect(row.outcome).toBe('Rental');
  });
});

describe('a settlement', () => {
  it('reads as the positive figure that moved, marked void once voided', () => {
    const settlement: OfficeSettlement = {
      settlementId: 's-1', number: 'SET-2026-000002', dealerId: 'd-1', dealerName: 'Petra Wheels', direction: 'Received',
      amount: jod(-15), paidOn: '2026-10-09', reference: 'TRX-9', note: null, recordedAt: '2026-10-09T09:00:00Z',
      recordedBy: null, payableCount: 3, isTest: null,
      void: { voidedAt: '2026-10-10T09:00:00Z', voidedBy: null, reason: null },
    };

    const row = settlementRow(settlement, 'office', words('en'), format);

    expect(row.direction).toBe('Received from the office');
    expect(row.amount).toBe('15 JOD');
    expect(row.amountTone).toBe('dim');
    expect(row.voided).toEqual({ when: '2026-10-10T09:00', by: null, reason: null });
  });
});

describe('money a settlement has moved', () => {
  it('speaks of what was owed in the past tense, because it no longer is', () => {
    expect(settledText(jod(12), 'admin', words('en'), format).text).toBe('Khadra owed the office 12 JOD');
    expect(settledText(jod(-5), 'admin', words('en'), format).text).toBe('The office owed Khadra 5 JOD');
    expect(settledText(jod(12), 'office', words('en'), format).text).toBe('Khadra owed you 12 JOD');
    expect(settledText(jod(-5), 'office', words('en'), format).text).toBe('You owed Khadra 5 JOD');
    expect(settledText(jod(12), 'admin', words('ar'), format).text).toBe('كانت خضرا تدين للمكتب بمبلغ 12 JOD');
    expect(settledText(jod(0), 'admin', words('en'), format).text).toBe('Nothing either way');
  });

  it('says a settled payable was owed, and an open one is owed', () => {
    const settlement = { settlementId: 's-1', number: 'TEST-SET-2026-000001', paidOn: '2026-10-09' };

    expect(payableRow(payable({ state: 'Settled', settlement }), 'admin', words('en'), format).net).toBe('Khadra owed the office 12 JOD');
    expect(payableRow(payable(), 'admin', words('en'), format).net).toBe('Khadra owes the office 12 JOD');
    expect(
      settlementLineRow({ payableId: 'p-1', bookingId: 'b-1', bookingReference: 'KH-ABCD1234', outcome: 'PenaltyKept', net: jod(12) }, 'admin', words('en'), format).net,
    ).toBe('Khadra owed the office 12 JOD');
  });

  it('states what moved as the positive figure beside its direction, or nothing when it netted', () => {
    expect(movedText(jod(12), words('en'), format)).toEqual({ text: '12 JOD', tone: 'ok' });
    expect(movedText(jod(-15), words('en'), format)).toEqual({ text: '15 JOD', tone: 'warn' });
    expect(movedText(jod(0), words('ar'), format).text).toBe('لا شيء على أيّ من الطرفين');
  });
});

describe('the finance figures', () => {
  it("states the server's sums, and counts the bookings behind them without claiming commission on each", () => {
    const group = financeGroup(
      {
        currency: 'JOD', isTest: true, commissionEarned: jod(132), keptFromDisputes: jod(0), officeMoney: jod(147), officeCharges: jod(0),
        paidToOffices: jod(12), receivedFromOffices: jod(0), owedToOffices: jod(3), owedByOffices: jod(0), payablesRecorded: 11,
      },
      words('en'),
      format,
    );

    expect(group.isTest).toBe(true);
    expect(group.cards[0]).toEqual({ label: 'Commission earned', value: '132 JOD', hint: 'From 11 bookings final in this span' });
    expect(group.cards.map((card) => card.value)).toEqual(['132 JOD', '0 JOD', '12 JOD', '0 JOD', '3 JOD', '0 JOD']);
    expect(
      financeGroup(
        {
          currency: 'JOD', isTest: false, commissionEarned: jod(6), keptFromDisputes: jod(0), officeMoney: jod(18), officeCharges: jod(0),
          paidToOffices: jod(0), receivedFromOffices: jod(0), owedToOffices: jod(12), owedByOffices: jod(0), payablesRecorded: 2,
        },
        words('ar'),
        format,
      ).cards[0].hint,
    ).toBe('من حجزين صارا نهائيين في هذه المدة');
  });
});

describe("a booking's office card", () => {
  it('is absent when nothing was paid, and says what is not final yet', () => {
    const base = {
      outcome: null, officeMoney: null, commission: null, charges: null, net: null, lines: [], finalAt: null,
      recordedAt: null, settlement: null, payableId: null, holds: null, blocks: null,
    };

    expect(officeCard({ ...base, state: 'NotApplicable' }, 'admin', words('en'), format)).toBeNull();
    expect(officeCard(null, 'admin', words('en'), format)).toBeNull();
    expect(officeCard({ ...base, state: 'Open' }, 'admin', words('en'), format)).toMatchObject({
      state: 'Not final yet',
      net: null,
      note: "Worked out once this booking's outcome is final.",
    });
  });
});

describe('a refused settlement', () => {
  it('carries the balance due now', () => {
    expect(currentAmountOf({ status: 409, error: { code: 'payables.balance_changed', currentAmount: { amount: 9.5, currency: 'JOD' } } }))
      .toEqual({ amount: 9.5, currency: 'JOD' });
    expect(currentAmountOf({ status: 409, error: { code: 'payables.nothing_due' } })).toBeNull();
  });
});
