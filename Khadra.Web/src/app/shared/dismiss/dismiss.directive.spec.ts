import { Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { DismissDirective } from './dismiss.directive';

@Component({
  imports: [DismissDirective],
  template: `
    <div id="menu" khDismiss [khDismissWhen]="open()" (khDismiss)="reasons.push($event); open.set(false)">
      <button id="trigger" type="button" (click)="open.set(!open())">toggle</button>
      @if (open()) { <div id="panel"><a id="inside" href="#">item</a></div> }
    </div>
    <p id="outside">elsewhere</p>
  `,
})
class HostComponent {
  readonly open = signal(false);
  readonly reasons: string[] = [];
}

const press = (element: Element) => element.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
const escape = () => document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));

describe('dismissing a header panel', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;
  let root: HTMLElement;

  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    fixture = TestBed.createComponent(HostComponent);
    document.body.appendChild(fixture.nativeElement);
    host = fixture.componentInstance;
    root = fixture.nativeElement;
    await fixture.whenStable();
    (root.querySelector('#trigger') as HTMLButtonElement).click();
    await fixture.whenStable();
  });

  it('closes on a press outside the panel and its trigger', () => {
    press(root.querySelector('#outside')!);
    expect(host.open()).toBe(false);
    expect(host.reasons).toEqual(['outside']);
  });

  it('closes on Esc, and says so, so focus can go back to the trigger', () => {
    escape();
    expect(host.open()).toBe(false);
    expect(host.reasons).toEqual(['escape']);
  });

  it('ignores a press inside the panel or on its own trigger, so the trigger stays a toggle', () => {
    press(root.querySelector('#inside')!);
    press(root.querySelector('#trigger')!);
    expect(host.open()).toBe(true);
    expect(host.reasons).toEqual([]);
  });

  it('stops listening once the panel is closed', async () => {
    escape();
    await fixture.whenStable();
    press(root.querySelector('#outside')!);
    escape();
    expect(host.reasons).toEqual(['escape']);
  });
});
