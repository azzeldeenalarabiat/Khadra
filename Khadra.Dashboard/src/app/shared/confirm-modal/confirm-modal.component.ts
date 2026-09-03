import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { toneClass } from '../../core/models/console.models';
import { IconComponent } from '../icon/icon.component';

/**
 * The console's single confirmation dialog, rendered once in the shell.
 *
 * Every destructive admin action is funnelled through it so the consequence is
 * always stated before it happens, and so the wording lives with the action
 * rather than being retyped per screen.
 */
@Component({
  selector: 'kh-confirm-modal',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './confirm-modal.component.html',
  imports: [IconComponent],
})
export class ConfirmModalComponent {
  private readonly ui = inject(ConsoleUiService);

  protected readonly modal = this.ui.modal;
  protected readonly toneClass = toneClass;

  protected close(): void {
    this.ui.closeModal();
  }

  protected confirm(): void {
    this.ui.confirmModal();
  }
}
