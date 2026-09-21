import { Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { inject } from '@angular/core';
import { ModalConfig, Toast, Tone } from '../models/console.models';
import { I18nService } from '../i18n/i18n.service';
import { problemMessage, snapshotProblem } from '../i18n/problem';

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
  private readonly i18n = inject(I18nService);
  private toastTimer?: ReturnType<typeof setTimeout>;

  readonly modal = signal<ModalConfig | null>(null);
  readonly toast = signal<Toast | null>(null);
  /** True while a confirm handler is in flight, so the dialog can disable itself. */
  readonly modalBusy = signal(false);

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
    this.pendingAction = action;
    this.pendingResult = result;
    this.modal.set(config);
  }

  closeModal(): void {
    if (this.modalBusy()) return;
    this.pendingAction = null;
    this.pendingResult = null;
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
      // decision landed, which for an approval is the worst thing to be unsure about.
      this.modalBusy.set(false);
      // Worded as it is shown, in the language on screen: a toast is gone in seconds, so there is no
      // refusal left standing to re-word on a switch. The server's English only ever reads in English.
      //
      // Through `problemMessage`, not the server's sentence alone. An Arabic console used to answer
      // every refusal with one line — "رُفض الطلب. لم يتغيّر شيء." — so a taken email address, a
      // malformed phone number, an expired session and a fallen-over service were indistinguishable
      // on screen, and the one thing the operator needed was the one thing thrown away.
      const t = this.i18n.t;
      this.showToast(
        t('common.thatDidNotGoThrough'),
        problemMessage(snapshotProblem(error), this.i18n.lang(), t) ??
          t('common.serviceDidNotRespond'),
        'bad',
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

/** Splits on the first separator only, so a body may itself contain one. */
function splitOnce(value: string, separator: string): [string, string] {
  const index = value.indexOf(separator);
  return index === -1
    ? [value, '']
    : [value.slice(0, index), value.slice(index + separator.length)];
}
