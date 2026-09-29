import { commissionRate } from '../../core/i18n/commission-rate';
import { MoneyFormat, MoneyLine, balanceLines, commissionText, depositText, paysOut } from '../../core/i18n/money-words';
import { refundLines } from '../../core/i18n/refund-words';
import { BookingFinancials } from '../../core/models/financials.api';
import { Translate } from './renter-documents.presenter';

/** The office's Financial section, ready to render. */
export interface OfficeMoney {
  readonly lines: readonly MoneyLine[];
  /** The records contradict one another: one neutral line, the figures still shown as they are. */
  readonly reviewing: boolean;
}

/**
 * The rental office's Financial section (payments Phase 4b), from the financial state's OFFICE
 * projection: booking money only, its own share of a dispute and any charge to it, never a processing
 * fee (owner decisions 3 and 8, 2026-09-26).
 *
 * Every figure and state is the server's. The deposit reads the calculator's state rather than a guess
 * from refund reasons (pre-launch item 166); the balance is what was due, with the cash the office
 * recorded beside it and never "collected" without a record; the commission carries its state, and a
 * booking that pays nothing out shows no payout line (pre-launch item 158). No refund-progress badge:
 * the office sees one payment, and "partly refunded" would invite "where is the rest?" — the share it
 * is not shown (the advisor, 2026-09-26).
 */
export function officeMoney(financials: BookingFinancials, t: Translate, format: MoneyFormat): OfficeMoney {
  const summary = financials.summary;
  // The office is shown only the payment that applied; there is at most one.
  const paid = financials.payments.find((payment) => payment.status === 'Applied') ?? null;
  const live = financials.balance.state === 'NotYetDue' || financials.balance.state === 'DueAtHandover';

  const lines: MoneyLine[] = [
    {
      k: t('dealerBooking.rentalLine', { count: summary.days, rate: format.money(summary.dailyRate) }),
      v: format.money(summary.rentalSubtotal),
    },
    { k: t('dealerBooking.deliveryFeeYours'), v: format.money(summary.deliveryFee) },
    { k: t('dealerBooking.securityDepositHeldPer'), v: format.money(summary.securityDeposit) },
    {
      // What was PAID, named after the payment that confirmed the booking: never a deposit when it was everything.
      k: paid?.purpose === 'FullPayment'
        ? t('dealerBooking.paidInFullByCard')
        : t('dealerBooking.depositPaidByCard', { percent: format.percent(summary.depositPercent) }),
      v: format.money(summary.paidOnline),
      hi: live,
    },
    ...balanceLines(t, financials.balance, format),
    // Booking money that went back, as the office reads it: a dispute decision's share is the customer's.
    ...refundLines(t, paid?.refunds, format.money),
  ];

  if (financials.deposit.state !== 'NotPaid') {
    lines.push({ k: t('common.deposit'), v: depositText(t, financials.deposit, format), dim: true });
    const decision = financials.deposit.decision;
    if (decision?.toOffice) lines.push({ k: t('dealerMoney.toYou'), v: format.money(decision.toOffice) });
    if (decision?.chargedToOffice) lines.push({ k: t('dealerMoney.chargedToYou'), v: format.money(decision.chargedToOffice) });
  }

  const commission = financials.commission;
  if (commission) {
    lines.push({
      k: t('dealerBooking.platformCommissionFrozen', {
        rate: commissionRate(t, format.percent(commission.percent), commission.basis),
      }),
      v: commissionText(t, commission, format),
    });
  }
  lines.push(...payoutLines(financials, t, format));

  return { lines, reviewing: financials.needsReview };
}

/**
 * What the booking comes to for the office, from the payables ledger (payments Phase 8): who owes whom, and where it
 * stands — being recorded, in the next payout, not due yet, or paid under a settlement. Before the outcome is final,
 * one line says when it will be known. A booking that comes to nothing either way shows no payout line (item 158).
 */
function payoutLines(financials: BookingFinancials, t: Translate, format: MoneyFormat): MoneyLine[] {
  const office = financials.office;
  const net = office?.net;
  if (!office || office.state === 'Open' || !net) {
    return paysOut(financials.commission) && office?.state !== 'NotApplicable'
      ? [{ k: t('dealerReports.netPayout'), v: t('dealerMoney.payoutOpen'), dim: true }]
      : [];
  }
  if (net.amount === 0) return [];

  const amount = format.money({ amount: Math.abs(net.amount), currency: net.currency });
  const lines: MoneyLine[] = [
    { k: t('dealerReports.netPayout'), v: t(net.amount > 0 ? 'payouts.net.toYou' : 'payouts.net.byYou', { amount }), hi: true },
  ];
  const settlement = office.settlement;
  const where =
    office.state === 'Settled' && settlement
      ? t('dealerMoney.payoutSettled', { number: settlement.number, day: format.day ? format.day(settlement.paidOn) : settlement.paidOn })
      : office.state === 'Due'
        ? t('dealerMoney.payoutDue')
        : office.state === 'Blocked' || office.state === 'OnHold'
          ? t('dealerMoney.payoutNotYetDue')
          : office.state === 'AwaitingRecord'
            ? t('dealerMoney.payoutAwaiting')
            : null;
  if (where) lines.push({ k: t('dealerMoney.payoutWhere'), v: where, dim: true });
  return lines;
}
