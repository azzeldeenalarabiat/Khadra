import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { TranslationKey } from '../../core/i18n/en';
import { I18nService } from '../../core/i18n/i18n.service';
import { IconName } from '../../shared/icon/icon-paths';
import { IconComponent } from '../../shared/icon/icon.component';

interface NotLiveCopy {
  readonly titleKey: TranslationKey;
  readonly icon: IconName;
  readonly bodyKey: TranslationKey;
  readonly pointKeys: readonly TranslationKey[];
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

  protected readonly t = inject(I18nService).t;

  protected readonly copy = computed<NotLiveCopy>(() =>
    this.kind() === 'reviews'
      ? {
          titleKey: 'nav.reviews',
          icon: 'star',
          bodyKey: 'notLive.reviews.body',
          pointKeys: [
            'notLive.reviews.point1',
            'notLive.reviews.point2',
            'notLive.reviews.point3',
          ],
        }
      : {
          titleKey: 'nav.notifications',
          icon: 'bell',
          bodyKey: 'notLive.notifications.body',
          pointKeys: [
            'notLive.notifications.point1',
            'notLive.notifications.point2',
            'notLive.notifications.point3',
          ],
        },
  );
}
