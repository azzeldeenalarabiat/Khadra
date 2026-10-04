// Loaded before anything Angular: `Router` is used below as a DI token, and its partially compiled
// decorators need the JIT compiler present or this spec silently contributes no tests.
import '@angular/compiler';
import { Injector } from '@angular/core';
import { Router } from '@angular/router';
import { describe, expect, it } from 'vitest';
import { LocalRefusal } from '../i18n/problem';
import { ModalConfig } from '../models/console.models';
import { ConsoleUiService } from './console-ui.service';

/**
 * The console's one dialog, and what it does with a refusal.
 *
 * A refusal used to go to a toast, and the toast could not be seen: the dialog is a native `<dialog>`
 * opened with `showModal()`, which sits in the browser's top layer above every z-index the toast could
 * have. An office pressed a wrong handover code it could not see refused three times and used three of
 * the customer's five tries (E2E F53). The refusal now stays with the dialog, which shows it.
 */
const CONFIG: ModalConfig = {
  icon: 'key',
  tone: 'ok',
  title: 'Hand over the car?',
  body: 'Body',
  confirm: 'Record pickup',
  result: { title: 'Done', body: '' },
};

function service(): ConsoleUiService {
  const injector = Injector.create({
    providers: [
      { provide: Router, useValue: { navigateByUrl: () => Promise.resolve(true) } },
      { provide: ConsoleUiService, useClass: ConsoleUiService },
    ],
  });
  return injector.get(ConsoleUiService);
}

const refusedBy = (status: number, body: Record<string, unknown>) => ({ status, error: body });

describe('ConsoleUiService — a refused dialog', () => {
  it('stays open, keeps the refusal as facts, and raises no toast that would sit under it', async () => {
    const ui = service();
    ui.openAction(CONFIG, async () => {
      throw refusedBy(400, { code: 'handover.code_invalid', attemptsRemaining: 4 });
    }, { title: 'Done', body: '' });

    await ui.confirmModal({ handoverCode: '000000' });

    expect(ui.modal()).toBe(CONFIG);
    expect(ui.modalBusy()).toBe(false);
    expect(ui.toast()).toBeNull();
    expect(ui.modalRefusal()).toEqual({
      kind: 'problem',
      problem: expect.objectContaining({ status: 400, code: 'handover.code_invalid', attemptsRemaining: 4 }),
    });
  });

  it('keeps a refusal the console decided itself as its key, so it is worded in the language on screen', async () => {
    const ui = service();
    const refusal = new LocalRefusal('dealerDecide.codeAndReason');
    ui.openAction(CONFIG, async () => {
      throw refusal;
    }, { title: 'Done', body: '' });

    await ui.confirmModal();

    expect(ui.modalRefusal()).toEqual({ kind: 'local', refusal });
  });

  it('clears the refusal when the next attempt starts, and a success closes with its toast', async () => {
    const ui = service();
    let attempt = 0;
    let seenDuringSecondAttempt: unknown = 'not reached';
    ui.openAction(CONFIG, async () => {
      attempt++;
      if (attempt === 1) throw refusedBy(400, { code: 'handover.code_invalid' });
      seenDuringSecondAttempt = ui.modalRefusal();
    }, { title: 'Pickup recorded', body: 'KH-TEST', tone: 'ok' });

    await ui.confirmModal();
    expect(ui.modalRefusal()).not.toBeNull();

    await ui.confirmModal();
    expect(seenDuringSecondAttempt).toBeNull();
    expect(ui.modal()).toBeNull();
    expect(ui.modalRefusal()).toBeNull();
    expect(ui.toast()).toEqual({ title: 'Pickup recorded', body: 'KH-TEST', tone: 'ok' });
    ui.dismissToast();
  });

  it('drops the refusal when the dialog closes or another one opens', async () => {
    const ui = service();
    const refuse = async () => {
      throw refusedBy(409, { code: 'payables.balance_changed' });
    };
    ui.openAction(CONFIG, refuse, { title: 'Done', body: '' });
    await ui.confirmModal();
    ui.closeModal();
    expect(ui.modalRefusal()).toBeNull();

    ui.openAction(CONFIG, refuse, { title: 'Done', body: '' });
    await ui.confirmModal();
    ui.openAction({ ...CONFIG, title: 'Another' }, async () => undefined, { title: 'Done', body: '' });
    expect(ui.modalRefusal()).toBeNull();
    expect(ui.modal()?.title).toBe('Another');
  });

  it('does not replace a dialog whose work is still in flight', async () => {
    const ui = service();
    let finish!: () => void;
    ui.openAction(CONFIG, () => new Promise<void>((resolve) => (finish = resolve)), { title: 'Done', body: '' });

    const pending = ui.confirmModal();
    ui.openAction({ ...CONFIG, title: 'Opened while busy' }, async () => undefined, { title: 'Other', body: '' });
    expect(ui.modal()).toBe(CONFIG);

    finish();
    await pending;
    expect(ui.modal()).toBeNull();
    expect(ui.toast()?.title).toBe('Done');
    ui.dismissToast();
  });
});
