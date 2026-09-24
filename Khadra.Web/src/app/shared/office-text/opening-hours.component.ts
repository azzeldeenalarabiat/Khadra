import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { GalleryDaySchedule } from '../../core/api/catalogue.api';
import { AppConfigService } from '../../core/config/app-config.service';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { groupHours, weekdayIn } from './hours';

/**
 * An office's week as the server sends it, told compactly: neighbouring days with the same hours share
 * a line, and today's line is marked. Day names come from `Intl`, never typed, and "today" is judged in
 * the platform's time zone from `/app-config`, not the visitor's.
 *
 * "Today" is read once, when the page is drawn: a tab left open past midnight keeps yesterday's mark
 * until it is reloaded. Its hosts render it with `ngSkipHydration`, because a server render and a
 * hydration that straddle midnight in Amman would otherwise disagree about which line is today.
 */
@Component({
  selector: 'kh-opening-hours',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './opening-hours.component.html',
})
export class OpeningHoursComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly format = inject(FormatService);
  private readonly appConfig = inject(AppConfigService);

  readonly days = input.required<readonly GalleryDaySchedule[]>();

  protected readonly runs = computed(() => groupHours(this.days()));
  protected readonly today = computed(() => {
    const zone = this.appConfig.config()?.timeZone;
    return zone ? weekdayIn(zone, new Date()) : null;
  });
}
