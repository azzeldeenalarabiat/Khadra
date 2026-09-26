import { httpResource } from '@angular/common/http';
import { Injectable, computed, signal } from '@angular/core';
import { PagedResult } from '../models/bookings.api';
import { AdminPayment, AdminPaymentListItem, AdminRefundListItem, PaymentVocabulary } from '../models/payments.api';

/**
 * Which refunds the queue shows: `live` — everything still owed, refused first — or one refund status,
 * as the server's vocabulary names it.
 */
export type RefundView = string;

/** The payments list's filters, each a value the API reads as it is. */
export interface PaymentFilters {
  readonly status: string;
  readonly purpose: string;
  readonly reference: string;
  /** Amman calendar days, `yyyy-MM-dd`, inclusive at both ends. */
  readonly from: string;
  readonly to: string;
  /** Set by a link from another screen (a dealership, a customer); no control edits them. */
  readonly dealerId: string;
  readonly customerId: string;
}

export const NO_PAYMENT_FILTERS: PaymentFilters = {
  status: '',
  purpose: '',
  reference: '',
  from: '',
  to: '',
  dealerId: '',
  customerId: '',
};

/**
 * The administrator's view of money across the platform (payments Phase 4b): every checkout attempt,
 * the refunds queue, and one payment's page. Read-only: nothing here retries a card or sends a refund —
 * the payment sweep does that, and a refused refund is sent again by it without a button.
 */
@Injectable({ providedIn: 'root' })
export class AdminPaymentsService {
  private readonly base = '/api/v1/admin';

  readonly filters = signal<PaymentFilters>(NO_PAYMENT_FILTERS);
  readonly page = signal(1);

  /** Only the filters that are set: an empty value would be a filter on nothing. */
  private readonly params = computed(() => {
    const params: Record<string, string | number> = { page: this.page(), pageSize: 25 };
    for (const [key, value] of Object.entries(this.filters())) {
      if (value.trim()) params[key] = value.trim();
    }
    return params;
  });

  /** Idle until a screen asks: the rail never pays for a list nobody is looking at. */
  private readonly showingPayments = signal(false);
  private readonly showingRefunds = signal(false);

  readonly payments = httpResource<PagedResult<AdminPaymentListItem>>(() =>
    this.showingPayments() ? { url: `${this.base}/payments`, params: this.params() } : undefined,
  );

  readonly refundView = signal<RefundView>('live');
  readonly refundPage = signal(1);

  readonly refunds = httpResource<PagedResult<AdminRefundListItem>>(() =>
    this.showingRefunds()
      ? {
          url: `${this.base}/refunds`,
          params: {
            // The API reads a missing status as the live queue; "live" itself is not a status.
            ...(this.refundView() === 'live' ? {} : { status: this.refundView() }),
            page: this.refundPage(),
            pageSize: 25,
          },
        }
      : undefined,
  );

  /** The filter words, asked of the server once a list is on screen. */
  readonly vocabulary = httpResource<PaymentVocabulary>(() =>
    this.showingPayments() || this.showingRefunds() ? `${this.base}/payments/vocabulary` : undefined,
  );

  /** The payment a detail screen is showing; null keeps the resource idle. */
  readonly viewing = signal<string | null>(null);

  readonly payment = httpResource<AdminPayment>(() => {
    const id = this.viewing();
    return id ? `${this.base}/payments/${id}` : undefined;
  });

  /** Which list the Payments screen is on; the other stays idle. */
  show(view: 'payments' | 'refunds' | null): void {
    this.showingPayments.set(view === 'payments');
    this.showingRefunds.set(view === 'refunds');
  }

  setFilters(filters: PaymentFilters): void {
    this.filters.set(filters);
    this.page.set(1);
  }

  setRefundView(view: RefundView): void {
    this.refundView.set(view);
    this.refundPage.set(1);
  }
}
