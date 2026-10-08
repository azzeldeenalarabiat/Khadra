import { Directive, ElementRef, afterNextRender, inject, output } from '@angular/core';

/**
 * The keyboard half of a `role="menu"` panel (WAI-ARIA menu button pattern): when it opens, focus goes to its first
 * item; ArrowDown and ArrowUp move between items, wrapping; Home and End go to the first and last; Tab leaves the menu,
 * which closes it. Esc is `khDismiss`'s, which also hands focus back to the button that opened it.
 *
 * Put it on the element carrying `role="menu"`, rendered only while the menu is open, so a server render — where every
 * panel is closed — never runs it. Items are the panel's `[role="menuitem"]` elements that are not disabled, read on
 * every key, so a menu whose items change while it is open stays right.
 */
@Directive({
  selector: '[khMenuKeys]',
  host: { '(keydown)': 'onKeydown($event)' },
})
export class MenuKeysDirective {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** Tab left the menu: the caller closes it, and focus goes wherever Tab was taking it. */
  readonly khMenuLeave = output<void>();

  constructor() {
    afterNextRender(() => this.items()[0]?.focus());
  }

  protected onKeydown(event: KeyboardEvent): void {
    const items = this.items();
    if (items.length === 0) return;
    const current = items.indexOf(event.target as HTMLElement);

    let next: number | null = null;
    switch (event.key) {
      case 'ArrowDown':
        next = current < 0 ? 0 : (current + 1) % items.length;
        break;
      case 'ArrowUp':
        next = current < 0 ? items.length - 1 : (current - 1 + items.length) % items.length;
        break;
      case 'Home':
        next = 0;
        break;
      case 'End':
        next = items.length - 1;
        break;
      case 'Tab':
        this.khMenuLeave.emit();
        return;
      default:
        return;
    }

    event.preventDefault();
    items[next].focus();
  }

  private items(): HTMLElement[] {
    return Array.from(
      this.host.nativeElement.querySelectorAll<HTMLElement>('[role="menuitem"]'),
    ).filter(
      (item) => !item.hasAttribute('disabled') && item.getAttribute('aria-disabled') !== 'true',
    );
  }
}
