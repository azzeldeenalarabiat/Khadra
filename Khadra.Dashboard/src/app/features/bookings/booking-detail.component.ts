import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import {
  BOOKING_CARDS,
  BOOKING_DETAIL,
  BOOKING_TIMELINE,
  VEHICLE_ROWS,
} from '../../core/data/details.data';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { TimelineComponent } from '../../shared/timeline/timeline.component';

/** One rental, from request through handover to settlement. */
@Component({
  selector: 'kh-booking-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './booking-detail.component.html',
  imports: [IconComponent, TimelineComponent],
})
export class BookingDetailComponent {
  private readonly ui = inject(ConsoleUiService);
  private readonly router = inject(Router);

  protected readonly booking = BOOKING_DETAIL;
  protected readonly cards = BOOKING_CARDS;
  protected readonly vehicleRows = VEHICLE_ROWS;
  protected readonly timeline = BOOKING_TIMELINE;

  protected goPayment(): void {
    void this.router.navigateByUrl('/payments/detail');
  }

  protected goDispute(): void {
    void this.router.navigateByUrl('/disputes/detail');
  }

  protected refund(): void {
    this.ui.openModal('refund');
  }
}
