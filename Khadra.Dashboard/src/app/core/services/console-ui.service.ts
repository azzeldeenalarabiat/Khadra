import { Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { inject } from '@angular/core';
import { MODALS } from '../data/modals.data';
import { ModalConfig, Toast, Tone } from '../models/console.models';

/**
 * Console-wide UI state: the open confirmation dialog and the transient toast.
 *
 * Row actions and buttons carry a small action string rather than a callback, so
 * data files stay serialisable and free of behaviour. This service is the single
 * place that interprets them:
 *
 *   nav:/route            navigate
 *   modal:<id>            open a confirmation dialog from MODALS
 *   toast:Title|Body      show a toast directly
 *   noop                  deliberately does nothing (placeholder in the design)
 */
@Injectable({ providedIn: 'root' })
export class ConsoleUiService {
  private readonly router = inject(Router);
  private toastTimer?: ReturnType<typeof setTimeout>;

  readonly modalId = signal<string | null>(null);
  readonly modal = signal<ModalConfig | null>(null);
  readonly toast = signal<Toast | null>(null);
  /** True while a confirm handler is in flight, so the dialog can disable itself. */
  readonly modalBusy = signal(false);

  // Set when a dialog was opened to perform real work. The fixture-driven dialogs leave it null and
  // keep their old behaviour of simply reporting a canned outcome.
  private pendingAction: ((values: Record<string, string>) => Promise<void>) | null = null;
  private pendingResult: { title: string; body: string; tone?: Tone } | null = null;

  run(action: string): void {
    if (action === 'noop') return;

    const [kind, rest] = splitOnce(action, ':');
    switch (kind) {
      case 'nav':
        void this.router.navigateByUrl(rest);
        return;
      case 'modal':
        this.openModal(rest);
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

  openModal(id: string): void {
    const config = MODALS[id];
    if (!config) return;
    this.pendingAction = null;
    this.pendingResult = null;
    this.modalId.set(id);
    this.modal.set(config);
  }

  /**
   * Opens a dialog that actually does something. The consequence is still stated before it happens
   * (the whole reason destructive admin actions go through this dialog at all); the difference is
   * that confirming now performs the work and reports what really happened.
   */
  openAction(
    config: ModalConfig,
    action: (values: Record<string, string>) => Promise<void>,
    result: { title: string; body: string; tone?: Tone },
  ): void {
    this.pendingAction = action;
    this.pendingResult = result;
    this.modalId.set(null);
    this.modal.set(config);
  }

  closeModal(): void {
    if (this.modalBusy()) return;
    this.pendingAction = null;
    this.pendingResult = null;
    this.modalId.set(null);
    this.modal.set(null);
  }

  /** Confirming a dialog closes it and reports the outcome as a toast. */
  async confirmModal(values: Record<string, string> = {}): Promise<void> {
    const config = this.modal();
    if (!config || this.modalBusy()) return;

    const action = this.pendingAction;
    const result = this.pendingResult;
    if (!action) {
      this.closeModal();
      this.showToast(config.result.title, config.result.body, config.result.tone ?? 'ok');
      return;
    }

    this.modalBusy.set(true);
    try {
      await action(values);
      this.modalBusy.set(false);
      this.closeModal();
      this.showToast(result!.title, result!.body, result!.tone ?? 'ok');
    } catch (error) {
      // The dialog stays open on failure: closing it would leave the admin unsure whether the
      // decision landed, which for an approval is the worst thing to be unsure about.
      this.modalBusy.set(false);
      const problem = error as { error?: { title?: string } };
      this.showToast(
        'That did not go through',
        problem.error?.title ?? 'The service did not respond.',
        'bad',
      );
    }
  }

  showToast(title: string, body: string, tone: Tone = 'ok'): void {
    this.toast.set({ title, body, tone });
    clearTimeout(this.toastTimer);
    this.toastTimer = setTimeout(() => this.toast.set(null), 4200);
  }

  dismissToast(): void {
    clearTimeout(this.toastTimer);
    this.toast.set(null);
  }
}

/** Splits on the first separator only, so a body may itself contain one. */
function splitOnce(value: string, separator: string): [string, string] {
  const index = value.indexOf(separator);
  return index === -1
    ? [value, '']
    : [value.slice(0, index), value.slice(index + separator.length)];
}
