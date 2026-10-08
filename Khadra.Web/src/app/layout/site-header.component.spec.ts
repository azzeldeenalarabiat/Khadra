import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { NotificationsService } from '../core/api/notifications.service';
import { I18nService } from '../core/i18n/i18n.service';
import { SessionService, SessionState } from '../core/session/session.service';
import { SiteHeaderComponent } from './site-header.component';

/**
 * The header's menus on a short or narrow screen (Wave 5, F92). On a phone, My account and Sign out live only at the
 * end of the drawer, and the drawer is part of the sticky header: taller than the screen, its last entries could not
 * be reached by scrolling or by Tab. The height cap is CSS, measured in a browser; what a unit test can hold is that
 * the rule is there, that the drawer still carries both actions, and that it closes the way the other panels do.
 */
describe('the site header menus (F92)', () => {
  let fixture: ComponentFixture<SiteHeaderComponent>;
  const state = signal<SessionState>({
    status: 'signed-in',
    user: { id: 'u1', email: 'renter@example.com', fullName: 'Test Renter', phone: '+962790000000', role: 'Customer', isEmailVerified: true, mustChangePassword: false },
  });

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: '**', children: [] }]),
        {
          provide: SessionService,
          useValue: {
            state,
            isSignedIn: () => state().status === 'signed-in',
            user: () => {
              const current = state();
              return current.status === 'signed-in' ? current.user : null;
            },
            signOut: async () => undefined,
          },
        },
        { provide: NotificationsService, useValue: { unread: signal(0) } },
      ],
    });
    fixture = TestBed.createComponent(SiteHeaderComponent);
    document.body.appendChild(fixture.nativeElement);
    await fixture.whenStable();
  });

  afterEach(() => {
    fixture.nativeElement.remove();
    TestBed.resetTestingModule();
  });

  const host = () => fixture.nativeElement as HTMLElement;
  const menuButton = () => host().querySelector<HTMLButtonElement>('button[aria-controls="site-drawer"]')!;
  const accountButton = () => host().querySelector<HTMLButtonElement>('button[aria-controls="account-menu"]')!;
  const drawer = () => host().querySelector<HTMLElement>('#site-drawer');
  const i18n = () => TestBed.inject(I18nService);

  async function openDrawer(): Promise<void> {
    menuButton().click();
    await fixture.whenStable();
    expect(drawer()).not.toBeNull();
  }

  it('keeps My account and Sign out in the drawer for a signed-in customer', async () => {
    await openDrawer();
    const entries = [...drawer()!.querySelectorAll('a, button')].map((entry) => entry.textContent!.trim());
    expect(entries).toContain(i18n().t('nav.account'));
    expect(entries.at(-1)).toBe(i18n().t('nav.signOut'));
    expect(menuButton().getAttribute('aria-expanded')).toBe('true');
  });

  it('closes the drawer on Esc and hands focus back to the menu button', async () => {
    await openDrawer();
    drawer()!.querySelector<HTMLElement>('a')!.focus();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    await fixture.whenStable();
    expect(drawer()).toBeNull();
    expect(document.activeElement).toBe(menuButton());
    expect(menuButton().getAttribute('aria-expanded')).toBe('false');
  });

  it('closes the drawer on a press outside the header, and not on one inside it', async () => {
    await openDrawer();
    drawer()!.dispatchEvent(new Event('pointerdown', { bubbles: true }));
    await fixture.whenStable();
    expect(drawer()).not.toBeNull();

    document.body.dispatchEvent(new Event('pointerdown', { bubbles: true }));
    await fixture.whenStable();
    expect(drawer()).toBeNull();
  });

  it('opens one panel at a time where the drawer and the account menu are both on screen', async () => {
    await openDrawer();
    accountButton().click();
    await fixture.whenStable();
    expect(drawer()).toBeNull();
    expect(host().querySelector('#account-menu')).not.toBeNull();

    menuButton().click();
    await fixture.whenStable();
    expect(host().querySelector('#account-menu')).toBeNull();
    expect(drawer()).not.toBeNull();
  });
});

/**
 * The sources, read from disk. The specs run under Node but carry no Node type definitions, so its file API is reached
 * at run time rather than imported.
 */
interface FileSystem {
  readFileSync(path: string, encoding: 'utf8'): string;
}
const nodeModule = (name: string): Promise<FileSystem> => import(/* @vite-ignore */ name);
const projectRoot = (globalThis as unknown as { process: { cwd(): string } }).process.cwd();

/** One top-level rule of the stylesheet, from its selector to the brace that closes it. */
function rule(stylesheet: string, selector: string): string {
  const start = stylesheet.indexOf(`\n${selector} {\n`);
  expect(start, selector).toBeGreaterThanOrEqual(0);
  return stylesheet.slice(start, stylesheet.indexOf('\n}\n', start));
}

describe('the header panels are held to the screen (F92)', () => {
  it.each(['.site-drawer', '.menu__panel'])('%s is capped under the header and scrolls itself', async (selector) => {
    const css = (await nodeModule('node:fs')).readFileSync(`${projectRoot}/src/styles/_layout.scss`, 'utf8');
    const body = rule(css, selector);
    expect(body).toContain('max-block-size: calc(100dvh - var(--header-h));');
    expect(body).toContain('overflow-y: auto;');
  });
});
