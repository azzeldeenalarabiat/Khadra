import { describe, expect, it } from 'vitest';
import { AR } from '../i18n/ar';
import { EN, TranslationKey } from '../i18n/en';
import { MessageParams } from '../i18n/language';
import { resolveMessage } from '../i18n/resolve';
import { FinanceSummary, PanelMoney } from '../models/dashboard.api';
import { financePanel } from './finance-panel.presenter';

/**
 * "Money in motion" (payments Phase 4b), against the REAL dictionaries: the month's flows, the stock
 * owed back now with the orphaned captures as a part of it, and test money said as such.
 */
const en = (key: TranslationKey, params?: MessageParams) => resolveMessage(EN[key], params, 'en-GB', false) ?? key;
// Arabic wraps every value in zero-width direction isolates (resolve.ts); compare the visible words.
const ar = (key: TranslationKey, params?: MessageParams) =>
  (resolveMessage(AR[key], params, 'ar-JO-u-nu-latn', true) ?? key).replace(/[\u2068\u2069]/g, '');
const format = {
  money: (value: PanelMoney) => `${value.amount} ${value.currency}`,
  calendarMonthYear: (iso: string) => (iso.startsWith('2026-09') ? 'September 2026' : iso),
};
const jod = (amount: number): PanelMoney => ({ amount, currency: 'JOD' });

const summary = (overrides: Partial<FinanceSummary> = {}): FinanceSummary => ({
  generatedAt: '2026-09-26T12:00:00Z',
  paymentMode: 'Sandbox',
  currency: 'JOD',
  thisMonth: {
    from: '2026-09-01',
    to: '2026-10-01',
    appliedToBookings: jod(612.75),
    paymentsApplied: 9,
    processingFeesCharged: jod(0),
    refundsSettled: jod(472),
    refundsSettledCount: 6,
  },
  rightNow: {
    refundsInProgress: jod(233.75),
    refundsInProgressCount: 4,
    refundsFailed: jod(0),
    refundsFailedCount: 0,
    orphanedCapturesOwed: jod(0),
    orphanedCapturesOwedCount: 0,
  },
  otherCurrencies: [],
  ...overrides,
});

describe('the money panel', () => {
  it('states the month and what is owed back now, and says test money is test money', () => {
    const panel = financePanel(summary(), en, format);

    expect(panel.mode).toBe('Test payments (sandbox)');
    expect(panel.sandbox).toBe(true);
    expect(panel.period).toBe('This month · September 2026');
    expect(panel.month.map((line) => [line.label, line.value, line.note])).toEqual([
      ['Applied to bookings', '612.75 JOD', '9 payments'],
      ['Card processing fees charged', '0 JOD', null],
      ['Refunds settled', '472 JOD', '6 refunds'],
    ]);
    expect(panel.now.map((line) => [line.label, line.value])).toEqual([
      ['Refunds on their way', '233.75 JOD'],
      ['Refunds refused — still owed', '0 JOD'],
      ['Included above: captures that could not be applied', '0 JOD'],
    ]);
    // The orphaned captures are a PART of the stock above, never a third figure to add.
    expect(panel.now[2].part).toBe(true);
    expect(panel.now[1].hi).toBe(false);
  });

  it('raises refused refunds and lists another currency apart, in Arabic', () => {
    const panel = financePanel(
      summary({
        paymentMode: 'Live',
        rightNow: { ...summary().rightNow, refundsFailed: jod(18), refundsFailedCount: 1 },
        otherCurrencies: [{ currency: 'USD', refundsSettledThisMonth: { amount: 0, currency: 'USD' }, refundsOutstanding: { amount: 25.4, currency: 'USD' } }],
      }),
      ar,
      format,
    );

    expect(panel.mode).toBe('مدفوعات فعلية');
    expect(panel.sandbox).toBe(false);
    expect(panel.now[1].hi).toBe(true);
    expect(panel.now[1].note).toBe('استرداد واحد');
    expect(panel.otherCurrencies).toEqual(['USD: 0 USD استُرد هذا الشهر · 25.4 USD مستحق']);
  });

  it('never names a mode it does not know as one it does', () => {
    expect(financePanel(summary({ paymentMode: 'Paused' }), en, format).mode).toBe('Paused');
    expect(financePanel(summary({ paymentMode: 'None' }), en, format).mode).toBe('Payments are not accepted');
  });
});
