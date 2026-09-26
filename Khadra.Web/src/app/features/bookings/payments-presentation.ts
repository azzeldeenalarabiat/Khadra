import { Money } from '../../core/api/common.api';
import { BookingFinancials, FinancialPayment, FinancialRefund } from '../../core/api/financials.api';
import { TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';

/**
 * The booking's "Payments" section in words (payments Phase 4, owner 2026-09-26). Everything it states
 * is the server's financial state — figures, states and statuses — so this only chooses sentences; it
 * never adds, subtracts or compares two amounts to decide what happened.
 */

type Translate = (key: TranslationKey, params?: MessageParams) => string;

/** The two things the page formats: money in its own currency, and an instant in Amman. */
export interface PaymentsFormat {
  money(value: Money | null | undefined): string;
  dateTime(iso: string): string;
}

export interface PaymentsLine {
  readonly label: string;
  readonly value: string;
}

export interface PaymentsBadge {
  readonly label: string;
  /** A badge modifier class: `badge--ok`, `badge--warn`, `badge--bad`, or none. */
  readonly tone: string;
}

export interface RefundRow {
  readonly id: string;
  readonly label: string;
  readonly date: string;
  readonly amount: string;
  /** Null for a status this site does not know: it says nothing rather than guess. */
  readonly badge: PaymentsBadge | null;
}

export interface PaymentRow {
  readonly id: string;
  readonly title: string;
  readonly date: string;
  readonly amount: string;
  /** Null for a refund progress this site does not know: it says nothing rather than guess. */
  readonly badge: PaymentsBadge | null;
  readonly fee: string | null;
  readonly note: string | null;
  readonly refunds: readonly RefundRow[];
}

export interface PaymentsView {
  /** Whether there is anything to show: a payment, or a deposit the booking holds or held. */
  readonly visible: boolean;
  readonly reviewing: boolean;
  readonly totals: readonly PaymentsLine[];
  readonly balance: PaymentsLine | null;
  readonly cash: readonly PaymentsLine[];
  readonly balanceText: string | null;
  readonly deposit: string | null;
  readonly depositBadge: PaymentsBadge | null;
  readonly payments: readonly PaymentRow[];
}

/** The refund reasons this site words; anything newer reads as a plain "Refund". */
const REFUND_REASONS: ReadonlySet<string> = new Set([
  'FreeCancellation',
  'PlatformCancellation',
  'EndedBeforePickup',
  'DisputeWindowClosed',
  'DisputeResolution',
  'OrphanedCapture',
]);

const DEPOSIT_STATES: ReadonlySet<string> = new Set([
  'Held',
  'AppliedToRental',
  'InSettlementWindow',
  'UnderDispute',
  'SettledWithRental',
  'ReturnedWithPayment',
  'HeldUntilWindowCloses',
  'HeldForAssessedPenalty',
  'HeldUnresolved',
  'Released',
]);

export function paymentsView(financials: BookingFinancials, t: Translate, format: PaymentsFormat): PaymentsView {
  const { summary, balance, deposit } = financials;
  const positive = (value: Money | null | undefined): value is Money => !!value && value.amount > 0;

  const totals: PaymentsLine[] = [];
  if (positive(summary.paidOnline)) totals.push({ label: t('payments.paidOnline'), value: format.money(summary.paidOnline) });
  if (positive(summary.processingFees)) totals.push({ label: t('pay.summaryFee'), value: format.money(summary.processingFees) });
  if (positive(summary.refunded)) totals.push({ label: t('booking.refundedTotal'), value: format.money(summary.refunded) });
  if (positive(summary.refundInProgress)) totals.push({ label: t('booking.refundOutstanding'), value: format.money(summary.refundInProgress) });
  if (positive(summary.refundDelayed)) totals.push({ label: t('payments.refundDelayedTotal'), value: format.money(summary.refundDelayed) });

  let balanceLine: PaymentsLine | null = null;
  let balanceText: string | null = null;
  switch (balance.state) {
    case 'DueAtHandover':
      balanceLine = { label: t('quote.balance'), value: format.money(balance.amount) };
      break;
    case 'CashAtHandover':
      // Never "paid" without a record: what was due, and beside it whatever cash the office recorded.
      balanceLine = { label: t('payments.dueAtPickup'), value: format.money(balance.amount) };
      break;
    case 'PaidInFull':
      balanceText = t('booking.paidInFullNothingDue');
      break;
    case 'NotDue':
      balanceText = t('payments.balanceNotDue');
      break;
  }
  const cash = balance.state === 'CashAtHandover'
    ? balance.cashRecorded.map((record) => ({
        label: t(record.handover === 'Return' ? 'payments.cashRecordedReturn' : 'payments.cashRecordedPickup'),
        value: format.money(record.amount),
      }))
    : [];

  return {
    // A deposit state counts only when there is one: an answer without it is not a deposit to show.
    visible: financials.payments.length > 0 || (!!deposit?.state && deposit.state !== 'NotPaid'),
    reviewing: financials.needsReview,
    totals,
    balance: balanceLine,
    cash,
    balanceText,
    // While the records contradict one another, the review notice is the one thing said about them: a
    // sentence read from them could be the contradiction itself (a refund the rule owes and nobody recorded).
    deposit: financials.needsReview ? null : depositSentence(financials, t, format),
    depositBadge: !financials.needsReview && deposit.refund ? refundBadge(deposit.refund, t) : null,
    payments: financials.payments.map((payment) => paymentRow(payment, t, format)),
  };
}

/** Where the deposit is, as one sentence; the only share of a dispute named is the customer's own. */
function depositSentence(financials: BookingFinancials, t: Translate, format: PaymentsFormat): string | null {
  const deposit = financials.deposit;
  const amount = format.money(deposit.amount);
  const date = deposit.windowEndsAt ? format.dateTime(deposit.windowEndsAt) : '';
  if (deposit.state === 'DecidedByDispute') {
    const share = deposit.decision?.toCustomer;
    return share && share.amount > 0
      ? t('payments.deposit.DecidedByDispute', { share: format.money(share), amount })
      : t('payments.deposit.DecidedByDisputeNothing', { amount });
  }
  return DEPOSIT_STATES.has(deposit.state)
    ? t(`payments.deposit.${deposit.state}` as TranslationKey, { amount, date })
    : null;
}

function paymentRow(payment: FinancialPayment, t: Translate, format: PaymentsFormat): PaymentRow {
  const orphaned = payment.status === 'Orphaned';
  const titleKey: TranslationKey = orphaned
    ? 'payments.kind.Orphaned'
    : payment.purpose === 'FullPayment'
      ? 'payments.kind.FullPayment'
      : payment.purpose === 'Deposit'
        ? 'payments.kind.Deposit'
        : 'payments.kind.other';
  const fee = payment.processingFee && payment.processingFee.amount > 0
    ? t('payments.includesFee', { amount: format.money(payment.processingFee) })
    : null;
  return {
    id: payment.paymentId,
    title: t(titleKey),
    date: format.dateTime(payment.occurredAt),
    amount: format.money(payment.amountCharged ?? payment.appliedToBooking),
    badge: progressBadge(payment.refundProgress, t),
    fee,
    note: orphaned ? t('payments.orphanNote') : null,
    refunds: payment.refunds.map((refund) => ({
      id: refund.refundId,
      label: t((REFUND_REASONS.has(refund.reason) ? `booking.refundReason.${refund.reason}` : 'booking.refundReason.other') as TranslationKey),
      date: format.dateTime(refund.settledAt ?? refund.failedAt ?? refund.requestedAt),
      amount: format.money(refund.amount),
      badge: refundBadge(refund, t),
    })),
  };
}

/** A payment's refunds, read as one status; a progress this site does not know is not worded. */
function progressBadge(progress: string, t: Translate): PaymentsBadge | null {
  switch (progress) {
    case 'None':
      return { label: t('booking.depositPaid'), tone: 'badge--ok' };
    case 'InProgress':
      return { label: t('booking.refundOutstanding'), tone: 'badge--warn' };
    case 'Delayed':
      return { label: t('booking.refundDelayed'), tone: 'badge--bad' };
    case 'Partial':
      return { label: t('payments.partlyRefunded'), tone: '' };
    case 'Complete':
      return { label: t('booking.refunded'), tone: '' };
    default:
      return null;
  }
}

/** One refund: on its way, back with the customer, or refused and still owed. Nothing for a status it does not know. */
function refundBadge(refund: Pick<FinancialRefund, 'status'>, t: Translate): PaymentsBadge | null {
  switch (refund.status) {
    case 'Settled':
      return { label: t('booking.refunded'), tone: 'badge--ok' };
    case 'Failed':
      return { label: t('booking.refundDelayed'), tone: 'badge--bad' };
    case 'Requested':
    case 'Sent':
      return { label: t('booking.refundInitiated'), tone: 'badge--warn' };
    default:
      return null;
  }
}
