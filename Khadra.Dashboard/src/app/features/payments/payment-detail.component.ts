import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { PAYMENT_DETAIL, PAYMENT_HISTORY, PAYMENT_ROWS } from '../../core/data/details.data';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { TimelineComponent } from '../../shared/timeline/timeline.component';

/** One transaction, with the full attempt history the support team needs. */
@Component({
  selector: 'kh-payment-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './payment-detail.component.html',
  imports: [IconComponent, TimelineComponent],
})
export class PaymentDetailComponent {
  private readonly ui = inject(ConsoleUiService);
  private readonly router = inject(Router);

  protected readonly payment = PAYMENT_DETAIL;
  protected readonly rows = PAYMENT_ROWS;
  protected readonly history = PAYMENT_HISTORY;

  protected goBooking(): void {
    void this.router.navigateByUrl('/bookings/detail');
  }

  protected refund(): void {
    this.ui.openModal('refund');
  }

  protected retry(): void {
    this.ui.showToast('Payment retried', 'PAY-99321 was sent to the processor again.');
  }
}
