import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { filter, map, startWith } from 'rxjs';
import { SCREEN_PARENTS, SCREEN_TITLES } from '../core/data/nav.data';
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

  private readonly path = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => this.toKey(event.urlAfterRedirects)),
      startWith(this.toKey(this.router.url)),
    ),
    { initialValue: 'dashboard' },
  );

  protected readonly title = computed(() => SCREEN_TITLES[this.path()] ?? 'Dashboard');

  protected readonly crumbs = computed<Crumb[]>(() => {
    const key = this.path();
    const trail: Crumb[] = [{ label: 'Admin', route: '/dashboard', last: false }];
    const parent = SCREEN_PARENTS[key];
    if (parent) {
      trail.push({ label: SCREEN_TITLES[parent] ?? parent, route: `/${parent}`, last: false });
    }
    trail.push({ label: SCREEN_TITLES[key] ?? 'Dashboard', route: `/${key}`, last: true });
    return trail;
  });

  /** '/dealers/review?x=1' becomes 'dealers/review'. */
  private toKey(url: string): string {
    return (
      url
        .split('?')[0]
        .split('#')[0]
        .replace(/^\/+|\/+$/g, '') || 'dashboard'
    );
  }
}
