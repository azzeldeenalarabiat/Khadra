import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  effect,
  inject,
  viewChild,
} from '@angular/core';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { toneClass } from '../../core/models/console.models';
import { IconComponent } from '../icon/icon.component';

/**
 * The console's single confirmation dialog, rendered once in the shell.
 *
 * Every destructive admin action is funnelled through it so the consequence is
 * always stated before it happens, and so the wording lives with the action
 * rather than being retyped per screen.
 *
 * It is a native `<dialog>` opened with `showModal()`, which gives us the focus
 * trap, the inert background, Escape-to-close and the restored focus for free.
 * A hand-rolled overlay would have to reimplement all four, and a dialog that
 * confirms suspensions and refunds is the last place to get that wrong.
 */
@Component({
  selector: 'kh-confirm-modal',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './confirm-modal.component.html',
  imports: [IconComponent],
})
export class ConfirmModalComponent {
  private readonly ui = inject(ConsoleUiService);
  private readonly dialog = viewChild<ElementRef<HTMLDialogElement>>('dlg');

  protected readonly modal = this.ui.modal;
  protected readonly busy = this.ui.modalBusy;

  /** What the admin typed or chose, keyed by field label. */
  private readonly values = new Map<string, string>();
  protected readonly toneClass = toneClass;

  constructor() {
    effect(() => {
      const el = this.dialog()?.nativeElement;
      if (!el) return;
      const wanted = !!this.modal();
      if (wanted && !el.open) el.showModal();
      else if (!wanted && el.open) el.close();
    });
  }

  protected close(): void {
    this.ui.closeModal();
  }

  /**
   * Escape fires `cancel` first and `close` second. We act on `cancel` and
   * close through our own state, so the signal and the element never disagree
   * — and so dismissal still works on an engine that is lazy about `close`.
   */
  protected cancel(event: Event): void {
    event.preventDefault();
    this.close();
  }

  /** The card stops its own clicks, so a click that reaches here is the backdrop. */
  protected backdropClick(): void {
    this.close();
  }

  protected setField(label: string, event: Event): void {
    this.values.set(
      label,
      (event.target as HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement).value,
    );
  }

  protected confirm(): void {
    void this.ui.confirmModal(Object.fromEntries(this.values));
  }
}
