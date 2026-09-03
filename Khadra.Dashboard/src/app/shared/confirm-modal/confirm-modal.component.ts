import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  effect,
  inject,
  signal,
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

  /** What the admin typed or chose, keyed by field label. Rebuilt for every dialog. */
  private readonly values = signal<Record<string, string>>({});
  protected readonly toneClass = toneClass;

  constructor() {
    effect(() => {
      const el = this.dialog()?.nativeElement;
      if (!el) return;
      const wanted = !!this.modal();
      if (wanted && !el.open) el.showModal();
      else if (!wanted && el.open) el.close();
    });

    // Seed the fields from the dialog that is opening, and drop whatever the last one held.
    //
    // These values used to survive between dialogs. An admin who typed a rejection reason, changed
    // their mind and cancelled, then opened "Request clarification" on any dealer would silently
    // send that stale rejection text as the clarification note — and it lands in an append-only
    // audit log against their name. Keying by field label made it worse: two dialogs that both say
    // "Note" shared the same entry.
    effect(() => {
      const config = this.modal();
      const seeded: Record<string, string> = {};
      for (const field of config?.fields ?? []) {
        seeded[field.label] =
          field.value ?? (field.type === 'select' ? (field.options?.[0] ?? '') : '');
      }
      this.values.set(seeded);
    });
  }

  /**
   * Every field must be answered before the decision can be sent. The server refuses an empty
   * reason anyway; catching it here means the admin is told which box to fill instead of being
   * handed a validation error after committing to the action.
   */
  protected readonly canConfirm = computed(() => {
    const fields = this.modal()?.fields ?? [];
    const values = this.values();
    return fields.every((field) => field.optional || (values[field.label] ?? '').trim().length > 0);
  });

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
    const value = (event.target as HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement)
      .value;
    this.values.update((current) => ({ ...current, [label]: value }));
  }

  protected confirm(): void {
    if (!this.canConfirm()) return;
    void this.ui.confirmModal({ ...this.values() });
  }
}
