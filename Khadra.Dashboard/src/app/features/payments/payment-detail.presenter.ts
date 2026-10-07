import { TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';
import { tookMoney } from '../../core/i18n/money-words';
import { refundReasonKey } from '../../core/i18n/refund-words';
import { EnumFamily } from '../../core/i18n/status-key';
import { Tone } from '../../core/models/console.models';
import { Money } from '../../core/models/fleet.api';
import { AdminPayment, PaymentIncident } from '../../core/models/payments.api';
import { attemptLabel } from './payments.presenter';

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
  /** Quiet for the same capture said again, a warning when that is only assumed, alarm for an incident. */
  readonly tone: Tone;
  readonly amount: string | null;
  /** Tied by the provider reference alone: it arrived before the payment was saved. */
  readonly byReference: boolean;
  /** The provider's id for the capture the notice reported (Wave 4, B1), shown as it is. */
  readonly captureReference: string | null;
}

/**
 * A capture incident (Wave 4, B1): what the notice said, what the payment had, and whether somebody has dealt
 * with the money at the provider.
 */
export interface PaymentIncidentView {
  readonly id: string;
  readonly kind: string;
  readonly open: boolean;
  readonly status: string;
  readonly tone: Tone;
  readonly detected: string;
  /** What the notice said was captured, and what it is measured against, in words. */
  readonly figures: readonly string[];
  readonly captureReference: string | null;
  /** For a capture on another attempt: that attempt's page. */
  readonly otherPaymentId: string | null;
  /** Who marked it handled, and when; null while it is open. */
  readonly handled: string | null;
  /** The administrator's account, as they typed it. */
  readonly note: string | null;
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
  /** Null when the API sent none at all; the section is then not drawn. */
  readonly incidents: readonly PaymentIncidentView[] | null;
  /** Open incidents, said once above everything: each is money that may have moved twice. */
  readonly incidentNotice: string | null;
  readonly bookingId: string | null;
}

const PAYMENT_TONES: Readonly<Record<string, Tone>> = { Applied: 'ok', Orphaned: 'warn', Failed: 'bad' };
const REFUND_TONES: Readonly<Record<string, Tone>> = { Settled: 'ok', Failed: 'bad', Requested: 'warn', Sent: 'warn' };

/**
 * What became of a provider event, as a colour (Wave 4, B1). The same capture said again is quiet; one only ASSUMED
 * to be the same, because no capture reference could be compared, is a warning; an incident — a second charge, a
 * contradiction, a capture another attempt holds — and a refund event nobody could match are alarms.
 */
const EVENT_TONES: Readonly<Record<string, Tone>> = {
  Acted: 'ok',
  Duplicate: 'dim',
  Ignored: 'dim',
  AssumedDuplicate: 'warn',
  Orphaned: 'warn',
  Unknown: 'warn',
  Unmatched: 'bad',
  AmountMismatch: 'bad',
  SecondCapture: 'bad',
  OtherAttempt: 'bad',
};

/** One incident in words. Every figure is the server's; nothing here compares the two amounts. */
function incidentView(incident: PaymentIncident, t: Translate, label: EnumLabel, format: PaymentPageFormat): PaymentIncidentView {
  const handledAt = incident.handledAt;
  const open = handledAt === null;
  return {
    id: incident.incidentId,
    kind: label('paymentIncidentKind', incident.kind),
    open,
    status: t(open ? 'paymentDetail.incidentOpen' : 'paymentDetail.incidentHandled'),
    tone: open ? 'bad' : 'ok',
    detected: t('paymentDetail.incidentDetected', { when: format.when(incident.detectedAt) }),
    figures: [
      t('paymentDetail.incidentReported', { amount: format.money(incident.reported) }),
      // A capture on another attempt is measured against what THIS attempt asked for; any other incident against
      // what this payment had already taken.
      t(incident.kind === 'CaptureOnAnotherAttempt' ? 'paymentDetail.incidentAskedFor' : 'paymentDetail.incidentAlreadyTaken', {
        amount: format.money(incident.expected),
      }),
    ],
    captureReference: incident.captureReference,
    otherPaymentId: incident.otherPaymentId,
    handled:
      handledAt === null
        ? null
        : incident.handledBy
          ? t('paymentDetail.incidentHandledBy', { name: incident.handledBy, when: format.when(handledAt) })
          : t('paymentDetail.incidentHandledAt', { when: format.when(handledAt) }),
    note: incident.handledNote,
  };
}

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

  const openIncidents = (page.incidents ?? []).filter((incident) => incident.handledAt === null).length;

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
    title: t('paymentDetail.title', { id: attemptLabel(payment.paymentId, payment.providerReference) }),
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
      tone: EVENT_TONES[event.outcome] ?? 'dim',
      amount: event.amount ? format.money(event.amount) : null,
      byReference: event.tiedBy === 'Reference',
      captureReference: event.captureReference ?? null,
    })),
    incidents: page.incidents ? page.incidents.map((incident) => incidentView(incident, t, label, format)) : null,
    incidentNotice: openIncidents > 0 ? t('paymentDetail.openIncidentsNotice', { count: openIncidents }) : null,
    bookingId: page.booking?.bookingId ?? null,
  };
}
