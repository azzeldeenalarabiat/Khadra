import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { GalleryDaySchedule } from '../../core/api/catalogue.api';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';

/** An office's week as the server sends it. Day names come from `Intl`, never typed. */
@Component({
  selector: 'kh-opening-hours',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './opening-hours.component.html',
})
export class OpeningHoursComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly format = inject(FormatService);
  readonly days = input.required<readonly GalleryDaySchedule[]>();
}
