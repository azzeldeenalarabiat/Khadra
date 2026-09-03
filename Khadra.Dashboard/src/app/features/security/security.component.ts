import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { LOGIN_HISTORY, SESSIONS } from '../../core/data/details.data';
import { toneClass } from '../../core/models/console.models';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { IconComponent } from '../../shared/icon/icon.component';

/** The admin's own account security, plus platform-wide sign-in activity. */
@Component({
  selector: 'kh-security',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './security.component.html',
  imports: [IconComponent],
})
export class SecurityComponent {
  private readonly ui = inject(ConsoleUiService);

  protected readonly sessions = SESSIONS;
  protected readonly history = LOGIN_HISTORY;
  protected readonly toneClass = toneClass;

  protected revoke(): void {
    this.ui.openModal('revoke-session');
  }

  protected enforce2fa(): void {
    this.ui.openModal('2fa');
  }
}
