import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { IconName } from '../../shared/icon/icon-paths';
import { IconComponent } from '../../shared/icon/icon.component';

interface NotLiveCopy {
  readonly title: string;
  readonly icon: IconName;
  readonly body: string;
  readonly points: readonly string[];
}

/**
 * Screens the design has and the platform does not yet: reviews and notifications.
 *
 * Both need a module that is not built (reviews persistence; a notification feed). Rather than show
 * invented ratings or a fake inbox, the page says plainly what will be here and what already
 * exists in its place, so a dealer testing the console is not misled about what is live.
 */
@Component({
  selector: 'kh-not-live',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './not-live.component.html',
  imports: [RouterLink, IconComponent],
})
export class NotLiveComponent {
  private readonly route = inject(ActivatedRoute);

  private readonly kind = toSignal(this.route.data.pipe(map((data) => data['kind'] as string)), {
    initialValue: this.route.snapshot.data['kind'] as string,
  });

  protected readonly copy = computed<NotLiveCopy>(() =>
    this.kind() === 'reviews'
      ? {
          title: 'Reviews',
          icon: 'star',
          body: 'Customer reviews are not live yet. When they are, every completed booking lets the customer rate your dealership and lets you rate the customer, and the ratings appear here and on your public page.',
          points: [
            'Reviews are tied to completed bookings only — no anonymous ratings.',
            'Until then, your public page says "No reviews yet" rather than showing a number.',
            'Nothing you do now affects a future rating.',
          ],
        }
      : {
          title: 'Notifications',
          icon: 'bell',
          body: 'A notification feed is not live yet. Today the dashboard already shows everything that needs your attention: pending requests, pickups and returns due, and overdue returns.',
          points: [
            'Booking requests appear on the dashboard the moment the customer pays the deposit.',
            'Staff invitations and password links go by email.',
            'Push and SMS alerts will arrive with the customer app.',
          ],
        },
  );
}
