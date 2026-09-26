import { TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';
import { tookMoney } from '../../core/i18n/money-words';
import { refundReasonKey } from '../../core/i18n/refund-words';
import { EnumFamily } from '../../core/i18n/status-key';
import { Tone } from '../../core/models/console.models';
import { Money } from '../../core/models/fleet.api';
import { AdminPayment } from '../../core/models/payments.api';

type Translate = (key: TranslationKey, params?: MessageParams) => string;
type EnumLabel = (family: EnumFamily, name: string | null | undefined) => string;

/** How the page prints a figure and an instant. */
export interface PaymentPageFormat {
  money(value: Money): string;
  when(iso: string): string;
}

export interface PaymentFact {
  readonly k: string;
  readonly v: string;
  /** A machine value — a provider reference — shown as it is, left to right. */
  readonly code?: boolean;
}

export interface PaymentRefundView {
  readonly id: string;
  readonly reason: string;
  readonly status: string;
  readonly tone: Tone;
  readonly amount: string;
  /** Booking money and processing fee side by side, as the server split them. */
  readonly split: string;
  readonly dates: readonly string[];
  readonly code: string | null;
  readonly providerReference: string | null;
  readonly disputeTicketId: string | null;
}

export interface ProviderEventView {
  readonly id: string;
  readonly when: string;
  readonly kind: string;
  readonly outcome: string;
  readonly amount: string | null;
  /** Tied by the provider reference alone: it arrived before the payment was saved. */
  readonly byReference: boolean;
}

export interface PaymentPage {
  readonly title: string;
  readonly subtitle: string;
  readonly status: string;
  readonly tone: Tone;
  readonly sandbox: boolean;
  /** Why an attempt took nothing, or why a capture is going back whole. */
  readonly notice: { readonly text: string; readonly tone: Tone } | null;
  readonly facts: readonly PaymentFact[];
  readonly refunds: readonly PaymentRefundView[];
  readonly events: readonly ProviderEventView[];
  readonly bookingId: string | null;
}

const PAYMENT_TONES: Readonly<Record<string, Tone>> = { Applied: 'ok', Orphaned: 'warn', Failed: 'bad' };
const REFUND_TONES: Readonly<Record<string, Tone>> = { Settled: 'ok', Failed: 'bad', Requested: 'warn', Sent: 'warn' };

/**
 * One payment's page in words (payments Phase 4b): the attempt as the booking's financial state
 * describes it, its refunds with booking money and fee apart, and every event the provider sent about
 * it. Every figure and state is the server's; nothing here adds or compares money.
 */
export function paymentPage(page: AdminPayment, t: Translate, label: EnumLabel, format: PaymentPageFormat): PaymentPage {
  const payment = page.payment;
  const purpose = label('paymentPurpose', payment.purpose);
  // What the attempt charged — or, when it took nothing, what its checkout asked for.
  const amount = payment.amountCharged ?? payment.appliedToBooking;

  const facts: PaymentFact[] = [
    { k: t('paymentDetail.status'), v: label('paymentStatus', payment.status) },
    { k: t('paymentDetail.purpose'), v: purpose },
    { k: t(tookMoney(payment.status) ? 'paymentDetail.charged' : 'paymentDetail.requested'), v: format.money(amount) },
  ];
  if (payment.processingFee) {
    // Refundable or kept is a fact about a fee that was CHARGED: on an attempt that took nothing, or a
    // zero fee, it is only the amount.
    const feeCharged = tookMoney(payment.status) && payment.processingFee.amount > 0;
    facts.push({
      k: t('paymentDetail.fee'),
      v: feeCharged
        ? t(payment.feeRefundable === false ? 'paymentDetail.feeKept' : 'paymentDetail.feeRefundable', {
            amount: format.money(payment.processingFee),
          })
        : format.money(payment.processingFee),
    });
  }
  facts.push(
    { k: t('paymentDetail.applied'), v: format.money(payment.appliedToBooking) },
    { k: t('paymentDetail.refundProgress'), v: label('refundProgress', payment.refundProgress) },
  );
  if (payment.providerReference) {
    facts.push({ k: t('paymentDetail.providerReference'), v: payment.providerReference, code: true });
  }
  if (payment.createdAt) facts.push({ k: t('paymentDetail.opened'), v: format.when(payment.createdAt) });
  facts.push({ k: t('paymentDetail.lastChange'), v: format.when(payment.occurredAt) });

  const notice =
    payment.status === 'Failed'
      ? {
          text: payment.failureCode
            ? t('paymentDetail.failedNotice', { code: payment.failureCode })
            : t('paymentDetail.failedNoCode'),
          tone: 'bad' as Tone,
        }
      : payment.status === 'Orphaned'
        ? { text: t('paymentDetail.orphanedNotice', { reason: payment.orphanReason ?? '—' }), tone: 'warn' as Tone }
        : null;

  return {
    title: t('paymentDetail.title', { id: payment.paymentId.slice(0, 8) }),
    subtitle: t('paymentDetail.subtitle', {
      purpose,
      reference: page.booking?.reference ?? t('payments.noBooking'),
      amount: format.money(amount),
    }),
    status: label('paymentStatus', payment.status),
    tone: PAYMENT_TONES[payment.status] ?? 'dim',
    sandbox: payment.isSandbox === true,
    notice,
    facts,
    refunds: payment.refunds.map((refund) => ({
      id: refund.refundId,
      reason: t(refundReasonKey(refund.reason)),
      status: label('refundStatus', refund.status),
      tone: REFUND_TONES[refund.status] ?? 'dim',
      amount: format.money(refund.amount),
      // The administrator's projection always splits a refund; a missing fee part is left unsaid, never invented.
      split: refund.feePart
        ? `${t('paymentDetail.bookingPart')}: ${format.money(refund.bookingPart)} · ${t('paymentDetail.feePart')}: ${format.money(refund.feePart)}`
        : `${t('paymentDetail.bookingPart')}: ${format.money(refund.bookingPart)}`,
      dates: [
        t('paymentDetail.recordedAt', { when: format.when(refund.requestedAt) }),
        ...(refund.sentAt ? [t('paymentDetail.sentAt', { when: format.when(refund.sentAt) })] : []),
        ...(refund.settledAt ? [t('paymentDetail.settledAt', { when: format.when(refund.settledAt) })] : []),
        ...(refund.failedAt ? [t('paymentDetail.failedAt', { when: format.when(refund.failedAt) })] : []),
      ],
      code: refund.failureCode,
      providerReference: refund.providerReference,
      disputeTicketId: refund.disputeTicketId,
    })),
    events: page.providerEvents.map((event) => ({
      id: event.providerEventId,
      when: format.when(event.receivedAt),
      kind: event.kind,
      outcome: label('providerEventOutcome', event.outcome),
      amount: event.amount ? format.money(event.amount) : null,
      byReference: event.tiedBy === 'Reference',
    })),
    bookingId: page.booking?.bookingId ?? null,
  };
}
