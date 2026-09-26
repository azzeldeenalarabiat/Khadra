import { commissionRate } from '../../core/i18n/commission-rate';
import { TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';
import { MoneyFormat, MoneyLine, balanceLines, commissionText, depositText, tookMoney } from '../../core/i18n/money-words';
import { refundReasonKey, refundStatusKey } from '../../core/i18n/refund-words';
import { EnumFamily } from '../../core/i18n/status-key';
import { Tone } from '../../core/models/console.models';
import { BookingFinancials, FinancialPayment } from '../../core/models/financials.api';

type Translate = (key: TranslationKey, params?: MessageParams) => string;
/** `I18nService.enumLabel`: a server enum in the reader's language, spelled out when this build has no word. */
type EnumLabel = (family: EnumFamily, name: string | null | undefined) => string;

/** One refund under a payment, as the administrator reads it. */
export interface AdminRefundLine {
  readonly k: string;
  readonly v: string;
  /** Booking money and processing fee side by side, when a fee went back inside the refund. */
  readonly split: string | null;
  /** The provider's word for a refusal. */
  readonly code: string | null;
}

/** One checkout attempt on the booking, every one of them — the ones that took no money included. */
export interface AdminPaymentLine {
  readonly paymentId: string;
  readonly title: string;
  readonly status: string;
  readonly tone: Tone;
  /** Null when nothing has gone back from it. */
  readonly progress: string | null;
  readonly when: string;
  readonly figures: string;
  readonly sandbox: boolean;
  /** The provider's failure code, or why a capture could not be applied. */
  readonly code: string | null;
  readonly refunds: readonly AdminRefundLine[];
}

export interface AdminMoney {
  readonly lines: readonly MoneyLine[];
  readonly payments: readonly AdminPaymentLine[];
  /** The records' contradictions, worded: shown under one banner, never hidden. */
  readonly issues: readonly string[];
  readonly reviewing: boolean;
}

const STATUS_TONES: Readonly<Record<string, Tone>> = {
  Applied: 'ok',
  Orphaned: 'warn',
  Failed: 'bad',
};

/**
 * The administrator's Money section (payments Phase 4b), from the financial state's ADMIN projection:
 * everything the booking froze, what the card was charged and what applied, every refund with its
 * booking money and fee apart, the balance and deposit as states, every share of a dispute, the
 * commission with its state, every attempt — the ones that took no money included — and the records'
 * contradictions. Every figure is the server's; nothing here adds, subtracts or compares money.
 */
export function adminMoney(
  financials: BookingFinancials,
  pickupMethod: string,
  t: Translate,
  label: EnumLabel,
  format: MoneyFormat,
): AdminMoney {
  const summary = financials.summary;
  const positive = (value: { readonly amount: number } | null | undefined): boolean => !!value && value.amount > 0;
  const lines: MoneyLine[] = [
    { k: t('adminBooking.dailyRateForDays', { count: summary.days }), v: format.money(summary.dailyRate) },
    { k: t('myBooking.rentalTotal'), v: format.money(summary.rentalSubtotal) },
  ];
  // Keyed on the pickup method, not the amount: free delivery is a real answer an office can give.
  if (pickupMethod === 'Delivery') lines.push({ k: t('vehicleWizard.deliveryFee'), v: format.money(summary.deliveryFee) });
  lines.push(
    { k: t('myBooking.totalPrice'), v: format.money(summary.bookingTotal) },
    {
      k: t('adminBooking.depositWithPercent', { percent: format.percent(summary.depositPercent) }),
      v: format.money(summary.requiredDeposit),
    },
    { k: t('adminMoney.paidOnline'), v: format.money(summary.paidOnline), hi: true },
  );
  if (positive(summary.processingFees)) lines.push({ k: t('adminMoney.processingFees'), v: format.money(summary.processingFees!) });
  if (positive(summary.chargedOnline)) lines.push({ k: t('adminMoney.chargedOnline'), v: format.money(summary.chargedOnline!) });
  if (positive(summary.refunded)) lines.push({ k: t('adminBooking.refundedTotal'), v: format.money(summary.refunded) });
  if (positive(summary.refundInProgress)) lines.push({ k: t('adminBooking.refundOutstanding'), v: format.money(summary.refundInProgress) });
  if (positive(summary.refundDelayed)) lines.push({ k: t('adminMoney.refundDelayed'), v: format.money(summary.refundDelayed), hi: true });
  lines.push(...balanceLines(t, financials.balance, format));

  if (financials.deposit.state !== 'NotPaid') {
    lines.push({ k: t('common.deposit'), v: depositText(t, financials.deposit, format) });
    const decision = financials.deposit.decision;
    if (decision) {
      if (decision.toCustomer) lines.push({ k: t('adminMoney.toCustomer'), v: format.money(decision.toCustomer) });
      if (decision.toOffice) lines.push({ k: t('adminMoney.toOffice'), v: format.money(decision.toOffice) });
      if (decision.keptByPlatform) lines.push({ k: t('adminMoney.keptByPlatform'), v: format.money(decision.keptByPlatform) });
      if (decision.chargedToOffice) lines.push({ k: t('adminMoney.chargedToOffice'), v: format.money(decision.chargedToOffice) });
    }
  }

  lines.push({ k: t('vehicleDetail.securityDeposit'), v: format.money(summary.securityDeposit) });
  const commission = financials.commission;
  if (commission) {
    lines.push({
      k: t('adminBooking.platformCommissionWithPercent', {
        rate: commissionRate(t, format.percent(commission.percent), commission.basis),
      }),
      v: commissionText(t, commission, format),
    });
  }

  return {
    lines,
    payments: financials.payments.map((payment) => paymentLine(payment, t, label, format)),
    issues: (financials.issues ?? []).map((code) => label('financialIssue', code)),
    reviewing: financials.needsReview,
  };
}

function paymentLine(payment: FinancialPayment, t: Translate, label: EnumLabel, format: MoneyFormat): AdminPaymentLine {
  const figures = [
    payment.amountCharged
      ? t(tookMoney(payment.status) ? 'adminMoney.charged' : 'adminMoney.requested', {
          amount: format.money(payment.amountCharged),
        })
      : null,
    payment.processingFee && payment.processingFee.amount > 0
      ? t('adminMoney.feeInside', { amount: format.money(payment.processingFee) })
      : null,
    payment.status === 'Applied' ? t('adminMoney.appliedAmount', { amount: format.money(payment.appliedToBooking) }) : null,
  ].filter((figure): figure is string => figure !== null);

  return {
    paymentId: payment.paymentId,
    title: label('paymentPurpose', payment.purpose),
    status: label('paymentStatus', payment.status),
    tone: STATUS_TONES[payment.status] ?? 'dim',
    progress: payment.refundProgress === 'None' ? null : label('refundProgress', payment.refundProgress),
    when: format.dateTime(payment.occurredAt),
    figures: figures.join(' · '),
    sandbox: payment.isSandbox === true,
    code: payment.failureCode ?? payment.orphanReason,
    refunds: payment.refunds.map((refund) => ({
      k: t(refundReasonKey(refund.reason)),
      v: t(refundStatusKey(refund), { amount: format.money(refund.amount) }),
      split: refund.feePart && refund.feePart.amount > 0
        ? t('adminMoney.refundSplit', { booking: format.money(refund.bookingPart), fee: format.money(refund.feePart) })
        : null,
      code: refund.failureCode,
    })),
  };
}
