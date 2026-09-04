import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { filter, map, startWith } from 'rxjs';
import { SCREEN_PARENTS, SCREEN_TITLES } from '../core/data/nav.data';
import { areaLabel, initialsOf } from '../core/models/user-display';
import { homeRouteFor } from '../core/guards/role.guards';
import { SessionService } from '../core/services/session.service';
import { IconComponent } from '../shared/icon/icon.component';

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
  imports: [RouterLink, IconComponent],
})
export class AdminTopbarComponent {
  private readonly router = inject(Router);
  private readonly session = inject(SessionService);

  private readonly path = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => this.toKey(event.urlAfterRedirects)),
      startWith(this.toKey(this.router.url)),
    ),
    { initialValue: 'dashboard' },
  );

  protected readonly title = computed(() => SCREEN_TITLES[this.path()] ?? 'Dashboard');
  protected readonly initials = computed(() => initialsOf(this.session.user() ?? null));
  // Two initials in a circle are not a name to a screen reader, so the chip carries the full one.
  protected readonly name = computed(() => this.session.user()?.fullName ?? '');

  protected readonly crumbs = computed<Crumb[]>(() => {
    const key = this.path();
    const user = this.session.user() ?? null;
    // The root crumb names the side of the platform you are on, and links to the home your role
    // actually has — a dealer sent to /dashboard is only bounced straight back.
    const trail: Crumb[] = [
      { label: areaLabel(user?.role), route: homeRouteFor(user), last: false },
    ];
    const parent = SCREEN_PARENTS[key];
    if (parent) {
      trail.push({ label: SCREEN_TITLES[parent] ?? parent, route: `/${parent}`, last: false });
    }
    trail.push({ label: SCREEN_TITLES[key] ?? 'Dashboard', route: `/${key}`, last: true });
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
