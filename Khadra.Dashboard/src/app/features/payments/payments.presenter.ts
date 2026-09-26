import { TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';
import { refundReasonKey } from '../../core/i18n/refund-words';
import { EnumFamily } from '../../core/i18n/status-key';
import { Tone } from '../../core/models/console.models';
import { Money } from '../../core/models/fleet.api';
import { AdminPaymentListItem, AdminRefundListItem } from '../../core/models/payments.api';

type Translate = (key: TranslationKey, params?: MessageParams) => string;
type EnumLabel = (family: EnumFamily, name: string | null | undefined) => string;

/** How the lists print a figure and an instant. */
export interface ListFormat {
  money(value: Money): string;
  when(iso: string): string;
}

export interface PaymentRow {
  readonly id: string;
  /** The first characters of the id, for a person to quote; the whole id is in the link. */
  readonly shortId: string;
  readonly created: string;
  readonly bookingId: string;
  readonly reference: string | null;
  readonly customer: string;
  readonly dealer: string;
  readonly purpose: string;
  readonly charged: string;
  readonly fee: string;
  readonly status: string;
  readonly tone: Tone;
  /** Null when nothing went back from it. */
  readonly progress: string | null;
  readonly sandbox: boolean;
}

export interface RefundRow {
  readonly id: string;
  readonly paymentId: string;
  readonly bookingId: string;
  readonly reference: string | null;
  readonly reason: string;
  readonly customer: string;
  readonly dealer: string;
  readonly amount: string;
  readonly status: string;
  readonly tone: Tone;
  /** Since when it has been owed — or, once it is back with the customer, when it came back. */
  readonly date: string;
  readonly code: string | null;
  readonly disputeTicketId: string | null;
  readonly sandbox: boolean;
}

const PAYMENT_TONES: Readonly<Record<string, Tone>> = { Applied: 'ok', Orphaned: 'warn', Failed: 'bad' };
const REFUND_TONES: Readonly<Record<string, Tone>> = { Settled: 'ok', Failed: 'bad', Requested: 'warn', Sent: 'warn' };

/**
 * The administrator's payments list and refunds queue in words (payments Phase 4b). Every figure and
 * status is the server's — the refund progress is the payment's own verdict — and a party that no
 * longer resolves is said to be gone, in the reader's language, never named by a stand-in.
 */
export function paymentRow(row: AdminPaymentListItem, t: Translate, label: EnumLabel, format: ListFormat): PaymentRow {
  return {
    id: row.paymentId,
    shortId: row.paymentId.slice(0, 8),
    created: format.when(row.createdAt),
    bookingId: row.bookingId,
    reference: row.bookingReference,
    customer: row.customerName ?? t('common.customerAccountClosed'),
    dealer: row.dealerName ?? t('common.dealerNoLongerOnPlatform'),
    purpose: label('paymentPurpose', row.purpose),
    charged: format.money(row.amountCharged),
    fee: format.money(row.processingFee),
    status: label('paymentStatus', row.status),
    tone: PAYMENT_TONES[row.status] ?? 'dim',
    progress: row.refundProgress === 'None' ? null : label('refundProgress', row.refundProgress),
    sandbox: row.isSandbox,
  };
}

export function refundRow(row: AdminRefundListItem, t: Translate, label: EnumLabel, format: ListFormat): RefundRow {
  return {
    id: row.refundId,
    paymentId: row.paymentId,
    bookingId: row.bookingId,
    reference: row.bookingReference,
    reason: t(refundReasonKey(row.reason)),
    customer: row.customerName ?? t('common.customerAccountClosed'),
    dealer: row.dealerName ?? t('common.dealerNoLongerOnPlatform'),
    amount: format.money(row.amount),
    status: label('refundStatus', row.status),
    tone: REFUND_TONES[row.status] ?? 'dim',
    date: format.when(row.status === 'Settled' && row.settledAt ? row.settledAt : row.requestedAt),
    code: row.failureCode,
    disputeTicketId: row.disputeTicketId,
    sandbox: row.isSandbox,
  };
}

/** A refund view's tab label: the live queue, or one status in its short form, spelled out when unknown. */
export function refundViewLabel(view: string, t: Translate, label: EnumLabel): string {
  if (view === 'live') return t('refunds.viewLive');
  const key = `refunds.view${view}`;
  return REFUND_VIEW_KEYS.has(key) ? t(key as TranslationKey) : label('refundStatus', view);
}

const REFUND_VIEW_KEYS: ReadonlySet<string> = new Set([
  'refunds.viewFailed',
  'refunds.viewRequested',
  'refunds.viewSent',
  'refunds.viewSettled',
]);
