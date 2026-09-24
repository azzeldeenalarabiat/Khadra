import { DOCUMENT, Directive, ElementRef, OnDestroy, effect, inject, input, output } from '@angular/core';

/**
 * Closes an open panel the way people expect a panel to close: a press anywhere outside it, or Esc.
 * Put it on the element that holds BOTH the trigger and the panel, so pressing the trigger again is a
 * toggle and not "outside". Listens only while `khDismissWhen` is true — a panel is only ever opened by
 * a press, so a server render, where every panel is closed, never adds a listener.
 */
@Directive({
  selector: '[khDismiss]',
})
export class DismissDirective implements OnDestroy {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly document = inject(DOCUMENT);

  readonly khDismissWhen = input(false);
  /** 'outside' for a press elsewhere, 'escape' for the key — so the caller can return focus on Esc. */
  readonly khDismiss = output<'outside' | 'escape'>();

  private readonly onPointer = (event: Event): void => {
    const target = event.target as Node | null;
    if (target && !this.host.nativeElement.contains(target)) this.khDismiss.emit('outside');
  };

  private readonly onKey = (event: KeyboardEvent): void => {
    if (event.key === 'Escape') {
      event.stopPropagation();
      this.khDismiss.emit('escape');
    }
  };

  constructor() {
    effect(() => {
      if (this.khDismissWhen()) this.listen();
      else this.stop();
    });
  }

  ngOnDestroy(): void {
    this.stop();
  }

  private listen(): void {
    // Capture, so a press that another element stops still counts as outside.
    this.document.addEventListener('pointerdown', this.onPointer, true);
    this.document.addEventListener('keydown', this.onKey);
  }

  private stop(): void {
    this.document.removeEventListener('pointerdown', this.onPointer, true);
    this.document.removeEventListener('keydown', this.onKey);
  }
}
