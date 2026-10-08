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
import { I18nService } from '../../core/i18n/i18n.service';
import { problemMessage } from '../../core/i18n/problem';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { ModalField, toneClass } from '../../core/models/console.models';
import { IconComponent } from '../icon/icon.component';
import { TranslationKey } from '../../core/i18n/en';
import { fieldShapeProblem } from './field-shape';

/** How a `line` field's input is drawn: an `email` or `tel` line is an identifier (Wave 5, F88). */
interface LineInput {
  readonly type: 'text' | 'email' | 'tel';
  readonly dir: 'ltr' | null;
  readonly autocapitalize: 'none' | null;
  readonly spellcheck: 'false' | null;
}

const PLAIN_LINE: LineInput = { type: 'text', dir: null, autocapitalize: null, spellcheck: null };

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
  private readonly i18n = inject(I18nService);
  protected readonly t = this.i18n.t;
  private readonly ui = inject(ConsoleUiService);
  private readonly dialog = viewChild<ElementRef<HTMLDialogElement>>('dlg');

  protected readonly modal = this.ui.modal;
  protected readonly busy = this.ui.modalBusy;

  /**
   * What the admin typed or chose, keyed by field NAME. Rebuilt for every dialog, and the controls are
   * bound to it — so what the dialog shows is what it will send.
   *
   * They were bound to the field's starting value instead, and the `@for` keeps its elements by field
   * name. A re-seeded dialog therefore emptied this record while the boxes went on showing the old
   * text — a note on screen that would be sent as nothing (E2E F52).
   */
  protected readonly values = signal<Record<string, string>>({});
  protected readonly toneClass = toneClass;

  /** Fields the admin has left at least once: a shape is judged out loud only then, never mid-word. */
  private readonly touched = signal<Record<string, boolean>>({});

  /** Each `email` or `tel` line whose value is plainly not one, keyed by field name (Wave 5, F88). */
  private readonly shapeProblems = computed(() => {
    const values = this.values();
    const problems: Record<string, TranslationKey> = {};
    for (const field of this.modal()?.fields ?? []) {
      const problem = fieldShapeProblem(field, values[field.name] ?? '');
      if (problem) problems[field.name] = problem;
    }
    return problems;
  });

  /** The words for a field's shape problem, once the field has been left; null otherwise. */
  protected shapeMessage(name: string): string | null {
    const problem = this.shapeProblems()[name];
    return problem && this.touched()[name] ? this.t(problem) : null;
  }

  /**
   * An address or a number is written left to right in Arabic too, and is neither capitalised nor corrected by the
   * phone's keyboard; its input type tells the browser and assistive technology what it holds.
   */
  protected lineInput(field: ModalField): LineInput {
    if (field.inputMode === 'email' || field.inputMode === 'tel') {
      return { type: field.inputMode, dir: 'ltr', autocapitalize: 'none', spellcheck: 'false' };
    }
    return PLAIN_LINE;
  }

  protected touch(name: string): void {
    this.touched.update((current) => ({ ...current, [name]: true }));
  }

  /**
   * Why the last attempt was refused, worded in the language on screen — inside the dialog, because a
   * toast cannot be seen over a modal `<dialog>` (E2E F53).
   */
  protected readonly refusal = computed(() => {
    const refused = this.ui.modalRefusal();
    if (!refused) return null;
    if (refused.kind === 'local') return this.t(refused.refusal.key, refused.refusal.params);
    return problemMessage(refused.problem, this.i18n.lang(), this.t) ?? this.t('common.serviceDidNotRespond');
  });

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
    // "Note" shared the same entry. Both are keyed by `name` now.
    effect(() => {
      const config = this.modal();
      const seeded: Record<string, string> = {};
      for (const field of config?.fields ?? []) {
        seeded[field.name] =
          field.value ?? (field.type === 'select' ? (field.options?.[0]?.value ?? '') : '');
      }
      this.values.set(seeded);
      this.touched.set({});
    });
  }

  /**
   * Every field must be answered before the decision can be sent. The server refuses an empty
   * reason anyway; catching it here means the admin is told which box to fill instead of being
   * handed a validation error after committing to the action. An address or a number that is plainly not one
   * holds the button too (Wave 5, F88), with its reason under the box once the box has been left.
   */
  protected readonly canConfirm = computed(() => {
    const fields = this.modal()?.fields ?? [];
    const values = this.values();
    const answered = fields.every((field) => field.optional || (values[field.name] ?? '').trim().length > 0);
    return answered && Object.keys(this.shapeProblems()).length === 0;
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

  protected setField(name: string, event: Event): void {
    const value = (event.target as HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement)
      .value;
    this.values.update((current) => ({ ...current, [name]: value }));
  }

  /**
   * Values are trimmed on the way out.
   *
   * `canConfirm` already judges a field by its trimmed length, so a value made only of spaces was
   * never sendable; what was still sendable was a real value with whitespace around it — and an
   * email address with a trailing space or newline is refused by the API's own model validation
   * before the domain normalises it. Nothing in this dialog is a value where the surrounding
   * whitespace means anything.
   */
  protected confirm(): void {
    if (!this.canConfirm()) return;
    const trimmed = Object.fromEntries(
      Object.entries(this.values()).map(([name, value]) => [name, value.trim()]),
    );
    void this.ui.confirmModal(trimmed);
  }
}
