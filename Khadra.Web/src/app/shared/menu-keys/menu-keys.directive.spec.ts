import { Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { MenuKeysDirective } from './menu-keys.directive';

@Component({
  imports: [MenuKeysDirective],
  template: `
    <button id="trigger" type="button" (click)="open.set(!open())">account</button>
    @if (open()) {
      <div id="menu" role="menu" khMenuKeys (khMenuLeave)="left = left + 1; open.set(false)">
        <a id="first" role="menuitem" href="#">Account</a>
        <a id="second" role="menuitem" href="#">Bookings</a>
        <button id="disabled" type="button" role="menuitem" disabled>Nothing</button>
        <button id="last" type="button" role="menuitem">Sign out</button>
      </div>
    }
  `,
})
class HostComponent {
  readonly open = signal(false);
  left = 0;
}

describe('a menu by keyboard', () => {
  let fixture: ComponentFixture<HostComponent>;
  let root: HTMLElement;

  const key = (name: string) =>
    document.activeElement!.dispatchEvent(
      new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true }),
    );
  const focused = () => document.activeElement?.id;

  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    fixture = TestBed.createComponent(HostComponent);
    document.body.appendChild(fixture.nativeElement);
    root = fixture.nativeElement;
    await fixture.whenStable();
    (root.querySelector('#trigger') as HTMLButtonElement).click();
    await fixture.whenStable();
  });

  afterEach(() => fixture.nativeElement.remove());

  it('puts focus on the first item when it opens', () => {
    expect(focused()).toBe('first');
  });

  it('moves with the arrows, wrapping, and skips a disabled item', () => {
    key('ArrowDown');
    expect(focused()).toBe('second');
    key('ArrowDown');
    expect(focused()).toBe('last');
    key('ArrowDown');
    expect(focused()).toBe('first');
    key('ArrowUp');
    expect(focused()).toBe('last');
  });

  it('goes to the ends with Home and End', () => {
    key('End');
    expect(focused()).toBe('last');
    key('Home');
    expect(focused()).toBe('first');
  });

  it('closes when Tab leaves it', async () => {
    key('Tab');
    await fixture.whenStable();
    expect(fixture.componentInstance.left).toBe(1);
    expect(root.querySelector('#menu')).toBeNull();
  });
});
