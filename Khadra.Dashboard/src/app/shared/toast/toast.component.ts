import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { Tone, toneClass } from '../../core/models/console.models';
import { IconComponent } from '../icon/icon.component';

/** Transient confirmation that an action completed. Auto-dismisses. */
@Component({
  selector: 'kh-toast',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './toast.component.html',
  imports: [IconComponent],
})
export class ToastComponent {
  private readonly ui = inject(ConsoleUiService);

  protected readonly toast = this.ui.toast;
  protected readonly toneClass = toneClass;

  protected icon(tone: Tone): string {
    if (tone === 'bad') return 'x-circle';
    if (tone === 'warn') return 'warning';
    return 'check-circle';
  }

  protected dismiss(): void {
    this.ui.dismissToast();
  }
}
