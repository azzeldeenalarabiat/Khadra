import { ChangeDetectionStrategy, Component, computed, effect, inject, input } from '@angular/core';
import { BookingFinancials } from '../../core/api/financials.api';
import { httpData } from '../../core/http/http-data';
import { snapshotProblem } from '../../core/http/problem';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { StatePanelComponent } from '../../shared/state/state-panel.component';
import { paymentsView } from './payments-presentation';

/**
 * A booking's "Payments" section (payments Phase 4): what was paid online, what went back and where
 * it is, the balance and the deposit as the server states them, and every payment with its refunds.
 * Read from `GET /api/v1/bookings/{id}/financials` — the page computes no figure of its own.
 *
 * Named "Payments" until issued invoices exist; Phase 5 renames it "Payments & Invoices" (owner,
 * 2026-09-26).
 */
@Component({
  selector: 'kh-booking-payments',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [StatePanelComponent],
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
