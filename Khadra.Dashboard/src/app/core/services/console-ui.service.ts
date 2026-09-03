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
    this.modalId.set(id);
    this.modal.set(config);
  }

  closeModal(): void {
    this.modalId.set(null);
    this.modal.set(null);
  }

  /** Confirming a dialog closes it and reports the outcome as a toast. */
  confirmModal(): void {
    const config = this.modal();
    if (!config) return;
    this.closeModal();
    this.showToast(config.result.title, config.result.body, config.result.tone ?? 'ok');
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
