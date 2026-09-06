import {
  ChangeDetectionStrategy,
  Component,
  HostListener,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { filter, map, startWith } from 'rxjs';
import { SCREEN_PARENTS, SCREEN_TITLES } from '../core/data/nav.data';
import { areaLabel, initialsOf } from '../core/models/user-display';
import { accountRouteFor, homeRouteFor } from '../core/guards/role.guards';
import { SessionService } from '../core/services/session.service';
import { I18nService } from '../core/i18n/i18n.service';
import { TranslationKey } from '../core/i18n/en';
import { LanguageSwitchComponent } from '../shared/language-switch/language-switch.component';
import { IconComponent } from '../shared/icon/icon.component';
import { NotificationsMenuComponent } from './notifications-menu.component';

interface Crumb {
  readonly label: string;
  readonly route: string;
  readonly last: boolean;
}

/**
 * Page header: breadcrumbs, title, global search and the account controls.
 *
 * The title and trail are derived from the current URL rather than pushed by each
 * screen, so a new route cannot forget to set them.
 */
@Component({
  selector: 'kh-admin-topbar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './admin-topbar.component.html',
  imports: [RouterLink, IconComponent, NotificationsMenuComponent, LanguageSwitchComponent],
})
export class AdminTopbarComponent {
  private readonly router = inject(Router);
  protected readonly t = inject(I18nService).t;
  private readonly session = inject(SessionService);

  /** Whether the account menu is showing. */
  protected readonly open = signal(false);

  private readonly path = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => this.toKey(event.urlAfterRedirects)),
      startWith(this.toKey(this.router.url)),
    ),
    { initialValue: 'dashboard' },
  );

  protected readonly title = computed(() => this.titleOf(this.path()));

  /**
   * The screen name in the current language.
   *
   * A route with no entry falls back to the raw path rather than to the word Dashboard, which is
   * what it did before: an unmapped screen used to claim to be the dashboard in the title bar and
   * the breadcrumb both.
   */
  private titleOf(key: string, fallback?: string): string {
    const titleKey: TranslationKey | undefined = SCREEN_TITLES[key];
    return titleKey ? this.t(titleKey) : (fallback ?? key);
  }
  protected readonly initials = computed(() => initialsOf(this.session.user() ?? null));
  // Two initials in a circle are not a name to a screen reader, so the chip carries the full one.
  protected readonly name = computed(() => this.session.user()?.fullName ?? '');
  protected readonly email = computed(() => this.session.user()?.email ?? '');
  protected readonly accountRoute = computed(() => accountRouteFor(this.session.user() ?? null));

  constructor() {
    // Navigating away closes it. A menu left open over the next screen is a menu the reader has to
    // dismiss before they can do anything.
    effect(() => {
      this.path();
      this.open.set(false);
    });
  }

  protected toggle(event: Event): void {
    // Stopped so the document listener below does not immediately close what this just opened.
    event.stopPropagation();
    this.open.update((open) => !open);
  }

  protected close(): void {
    this.open.set(false);
  }

  /** Clicking anywhere else closes it, which is what every menu on every other site does. */
  @HostListener('document:click')
  protected onDocumentClick(): void {
    if (this.open()) this.open.set(false);
  }

  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    if (this.open()) this.open.set(false);
  }

  protected async signOut(): Promise<void> {
    this.open.set(false);
    await this.session.signOut();
    await this.router.navigateByUrl('/sign-in');
  }

  protected readonly crumbs = computed<Crumb[]>(() => {
    const key = this.path();
    const user = this.session.user() ?? null;
    // The root crumb names the side of the platform you are on, and links to the home your role
    // actually has — a dealer sent to /dashboard is only bounced straight back.
    const trail: Crumb[] = [
      { label: areaLabel(user?.role, this.t), route: homeRouteFor(user), last: false },
    ];
    const parent = SCREEN_PARENTS[key];
    if (parent) {
      trail.push({ label: this.titleOf(parent, parent), route: `/${parent}`, last: false });
    }
    trail.push({ label: this.titleOf(key), route: `/${key}`, last: true });
    return trail;
  });

  /**
   * '/dealers/019a…?x=1' becomes 'dealers/:id'.
   *
   * Record ids are collapsed so one table entry titles every record of a kind. Without it a detail
   * route falls through to the default and the page announces itself as "Dashboard".
   */
  private toKey(url: string): string {
    const path = url
      .split('?')[0]
      .split('#')[0]
      .replace(/^\/+|\/+$/g, '');
    if (!path) return 'dashboard';

    return path
      .split('/')
      .map((segment) => (UUID.test(segment) ? ':id' : segment))
      .join('/');
  }
}

/** Ids are UUIDv7 from the API, but any UUID shape counts as "a record, not a screen". */
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
