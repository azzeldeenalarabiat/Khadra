import { ChangeDetectionStrategy, Component, computed, effect, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { BookingFinancialDocuments } from '../../core/api/financial-documents.api';
import { httpData } from '../../core/http/http-data';
import { snapshotProblem } from '../../core/http/problem';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { StatePanelComponent } from '../../shared/state/state-panel.component';
import { invoiceRow, preparingRow } from '../invoices/invoice-presentation';

/**
 * The booking's issued documents inside its "Payments & Invoices" card (payments Phase 5b): each receipt
 * and statement version, opening its own page, and what the server says is still being prepared. Read
 * from `GET /api/v1/bookings/{id}/financial-documents`, with its own states: a failure here never hides
 * the payment figures above it, and a booking with nothing to list shows nothing.
 *
 * No timer: documents are issued on the server's own schedule, and a page that promised "in a minute"
 * would be printing a server setting. "Check again" reads this list once more.
 */
@Component({
  selector: 'kh-booking-invoices',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, StatePanelComponent],
  templateUrl: './booking-invoices.component.html',
})
export class BookingInvoicesComponent {
  protected readonly i18n = inject(I18nService);
  private readonly format = inject(FormatService);

  readonly bookingId = input.required<string>();
  /** The payments card's own signal that the booking's money may have moved. */
  readonly version = input<string>('');

  protected readonly documents = httpData<BookingFinancialDocuments>(() => {
    const id = this.bookingId();
    return /^[0-9a-f-]{36}$/i.test(id) ? `/api/v1/bookings/${id}/financial-documents` : undefined;
  });
  protected readonly problem = computed(() => (this.documents.error() ? snapshotProblem(this.documents.error()) : null));
  protected readonly rows = computed(() =>
    (this.documents.value()?.documents ?? []).map((row) =>
      invoiceRow(row, this.i18n.isArabic(), this.i18n.t.bind(this.i18n), this.format),
    ),
  );
  protected readonly preparing = computed(() =>
    (this.documents.value()?.beingPrepared ?? []).map((pending) => preparingRow(pending, this.i18n.t.bind(this.i18n), this.format)),
  );

  constructor() {
    let seen: string | null = null;
    effect(() => {
      const version = this.version();
      if (seen !== null && version !== seen) this.documents.reload();
      seen = version;
    });
  }
}
