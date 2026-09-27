import { ChangeDetectionStrategy, Component, computed, effect, inject, input } from '@angular/core';
import { BookingFinancials } from '../../core/api/financials.api';
import { httpData } from '../../core/http/http-data';
import { snapshotProblem } from '../../core/http/problem';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { StatePanelComponent } from '../../shared/state/state-panel.component';
import { BookingInvoicesComponent } from './booking-invoices.component';
import { paymentsView } from './payments-presentation';

/**
 * A booking's "Payments & Invoices" section (payments Phases 4 and 5): what was paid online, what went
 * back and where it is, the balance and the deposit as the server states them, every payment with its
 * refunds — read from `GET /api/v1/bookings/{id}/financials`, the page computing no figure of its own —
 * and, beneath them, the booking's issued receipts and statements (`kh-booking-invoices`), which read
 * their own endpoint and fail on their own.
 *
 * Named "Payments" in Phase 4 and "Payments & Invoices" once documents exist (owner decision 6,
 * confirmed 2026-09-27).
 */
@Component({
  selector: 'kh-booking-payments',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [StatePanelComponent, BookingInvoicesComponent],
  templateUrl: './booking-payments.component.html',
})
export class BookingPaymentsComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly format = inject(FormatService);

  readonly bookingId = input.required<string>();
  /**
   * Changes whenever the booking's money may have moved — its status, whether it is paid, a refund's
   * status — as the page last read the booking. The section reads its financial state again then,
   * rather than on every one of the page's own refreshes.
   */
  readonly version = input<string>('');

  protected readonly financials = httpData<BookingFinancials>(() => {
    const id = this.bookingId();
    return /^[0-9a-f-]{36}$/i.test(id) ? `/api/v1/bookings/${id}/financials` : undefined;
  });
  protected readonly problem = computed(() => (this.financials.error() ? snapshotProblem(this.financials.error()) : null));
  protected readonly view = computed(() => {
    const financials = this.financials.value();
    return financials ? paymentsView(financials, this.i18n.t.bind(this.i18n), this.format) : null;
  });

  constructor() {
    let seen: string | null = null;
    effect(() => {
      const version = this.version();
      if (seen !== null && version !== seen) this.financials.reload();
      seen = version;
    });
  }
}
