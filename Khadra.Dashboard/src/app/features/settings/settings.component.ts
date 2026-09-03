import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { SETTINGS_GROUPS } from '../../core/data/details.data';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { IconComponent } from '../../shared/icon/icon.component';

/**
 * Platform business rules (spec 2).
 *
 * These are the numbers the backend reads through IBusinessRulesProvider, and a
 * change applies only to bookings created afterwards: an existing booking keeps
 * the terms it was made under. The confirmation dialog says so before saving.
 */
@Component({
  selector: 'kh-settings',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './settings.component.html',
  imports: [IconComponent],
})
export class SettingsComponent {
  private readonly ui = inject(ConsoleUiService);

  protected readonly groups = SETTINGS_GROUPS;

  protected change(): void {
    this.ui.openModal('save-setting');
  }
}
