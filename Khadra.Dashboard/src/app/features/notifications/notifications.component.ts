import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NOTIFICATIONS, NOTIFICATION_CATEGORIES } from '../../core/data/details.data';
import { toneClass } from '../../core/models/console.models';
import { IconComponent } from '../../shared/icon/icon.component';

/** Operational alerts, grouped by the part of the platform that raised them. */
@Component({
  selector: 'kh-notifications',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './notifications.component.html',
  imports: [RouterLink, IconComponent],
})
export class NotificationsComponent {
  protected readonly categories = NOTIFICATION_CATEGORIES;
  protected readonly items = NOTIFICATIONS;
  protected readonly toneClass = toneClass;

  protected readonly activeCategory = signal<string>('All');

  protected select(label: string): void {
    this.activeCategory.set(label);
  }
}
