import { Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { inject } from '@angular/core';
import { ModalConfig, Toast, Tone } from '../models/console.models';
import { LocalRefusal, ProblemSnapshot, snapshotProblem } from '../i18n/problem';

/**
 * Console-wide UI state: the open confirmation dialog and the transient toast.
 *
 * Table row actions carry a small action string rather than a callback, so a row stays serialisable
 * and free of behaviour. This service is the single place that interprets them:
 *
 *   nav:/route            navigate
 *   toast:Title|Body      show a toast directly
 *
 * There was a third form, `modal:<id>`, which opened a dialog from a table of canned wording and
 * reported a canned outcome without doing anything. It went with the sample data: a dialog that
 * says "Dealer approved" and approves nobody is the most convincing lie the console could tell.
 * Every dialog now arrives through `openAction`, with the work it performs attached.
 */
@Injectable({ providedIn: 'root' })
export class ConsoleUiService {
  private readonly router = inject(Router);
  private toastTimer?: ReturnType<typeof setTimeout>;

  readonly modal = signal<ModalConfig | null>(null);
  readonly toast = signal<Toast | null>(null);
  /** True while a confirm handler is in flight, so the dialog can disable itself. */
  readonly modalBusy = signal(false);
  /**
   * Why the open dialog's work was refused, shown INSIDE the dialog.
   *
   * It used to be a toast, and the toast could not be seen: the dialog is a native `<dialog>` opened
   * with `showModal()`, which puts it and its backdrop in the browser's top layer, above every z-index
   * the toast could have. An office pressing a refusal it could not see used three of a customer's five
   * handover tries (E2E F53). Held as facts, not words, so a language switch re-words it; cleared by the
   * next attempt, by closing, and by the next dialog.
   */
  readonly modalRefusal = signal<ModalRefusal | null>(null);

  // The work the open dialog will do when confirmed. A dialog cannot be opened without one.
  private pendingAction: ((values: Record<string, string>) => Promise<ActionOutcome>) | null = null;
  private pendingResult: { title: string; body: string; tone?: Tone } | null = null;

  run(action: string): void {
    const [kind, rest] = splitOnce(action, ':');
    switch (kind) {
      case 'nav':
        void this.router.navigateByUrl(rest);
        return;
      case 'toast': {
        const [title, body] = splitOnce(rest, '|');
        this.showToast(title, body);
        return;
      }
      default:
        return;
    }
  }

  /**
   * Opens a dialog that actually does something. The consequence is still stated before it happens
   * (the whole reason destructive admin actions go through this dialog at all); the difference is
   * that confirming now performs the work and reports what really happened.
   */
  openAction(
    config: ModalConfig,
    action: (values: Record<string, string>) => Promise<ActionOutcome>,
    result: { title: string; body: string; tone?: Tone },
  ): void {
    // A dialog whose work is in flight is not replaced under it: its answer would arrive for a dialog
    // nobody can see any more, and the new one would be re-seeded while its first request was still
    // reading the old values.
    if (this.modalBusy()) return;
    this.pendingAction = action;
    this.pendingResult = result;
    this.modalRefusal.set(null);
    this.modal.set(config);
  }

  closeModal(): void {
    if (this.modalBusy()) return;
    this.pendingAction = null;
    this.pendingResult = null;
    this.modalRefusal.set(null);
    this.modal.set(null);
  }

  /** Confirming a dialog closes it and reports the outcome as a toast. */
  async confirmModal(values: Record<string, string> = {}): Promise<void> {
    const config = this.modal();
    if (!config || this.modalBusy()) return;

    const action = this.pendingAction;
    const result = this.pendingResult;
    if (!action || !result) {
      this.closeModal();
      return;
    }

    this.modalBusy.set(true);
    this.modalRefusal.set(null);
    try {
      // What the work itself found, where that differs from what the dialog promised. An action
      // that reports nothing keeps the wording it was opened with; one that learned something the
      // caller could not know in advance — an invitation whose email the relay refused — says so
      // HERE, because a toast it raised on its own would be overwritten by this line a moment later.
      const outcome = await action(values);
      this.modalBusy.set(false);
      this.closeModal();
      this.showToast(
        outcome?.title ?? result.title,
        outcome?.body ?? result.body,
        outcome?.tone ?? result.tone ?? 'ok',
      );
    } catch (error) {
      // The dialog stays open on failure: closing it would leave the admin unsure whether the
      // decision landed, which for an approval is the worst thing to be unsure about. So the refusal
      // is shown in the dialog itself, where the operator is looking, and Confirm is usable again for
      // a corrected attempt. The dialog words it through `problemMessage`, in the language on screen.
      this.modalBusy.set(false);
      this.modalRefusal.set(
        error instanceof LocalRefusal
          ? { kind: 'local', refusal: error }
          : { kind: 'problem', problem: snapshotProblem(error) },
      );
    }
  }

  /**
   * A refusal stays up longer than a confirmation.
   *
   * 4.2 seconds is right for "Administrator deactivated" — the reader already knows what they did.
   * It is not right for a sentence naming what was wrong and what to do about it, which is a
   * sentence somebody has to read, in Arabic or English, while looking at a form they are about to
   * correct.
   */
  showToast(title: string, body: string, tone: Tone = 'ok'): void {
    this.toast.set({ title, body, tone });
    clearTimeout(this.toastTimer);
    this.toastTimer = setTimeout(() => this.toast.set(null), tone === 'bad' ? 9000 : 4200);
  }

  dismissToast(): void {
    clearTimeout(this.toastTimer);
    this.toast.set(null);
  }
}

/**
 * What a confirmed action may say about itself, overriding the wording the dialog was opened with.
 *
 * `void` is the normal case: the dialog already stated the consequence, and the outcome is that it
 * happened. An override is for what only the work can know — a record created but an email the
 * relay would not take — which the dialog could not have predicted and must not paper over.
 */
export type ActionOutcome = { title?: string; body?: string; tone?: Tone } | void;

/**
 * Why a dialog's work was refused: the server's answer, kept as facts, or a refusal the console decided
 * before sending anything. Either way the dialog chooses the words, in the language on screen.
 */
export type ModalRefusal =
  | { readonly kind: 'problem'; readonly problem: ProblemSnapshot }
  | { readonly kind: 'local'; readonly refusal: LocalRefusal };

/** Splits on the first separator only, so a body may itself contain one. */
function splitOnce(value: string, separator: string): [string, string] {
  const index = value.indexOf(separator);
  return index === -1
    ? [value, '']
    : [value.slice(0, index), value.slice(index + separator.length)];
}
