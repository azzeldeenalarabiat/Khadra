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
import { AdminDashboardService } from '../core/services/admin-dashboard.service';
import { DealerConsoleService } from '../core/services/dealer-console.service';
import { SessionService } from '../core/services/session.service';
import { loaded } from '../core/services/loaded';
import {
  NotificationRow,
  toAdminNotifications,
  toDealerNotifications,
} from '../core/services/notifications.presenter';
import { IconComponent } from '../shared/icon/icon.component';

/**
 * The bell beside the account chip, and the panel it opens.
 *
 * There is no Notifications context on this platform — no aggregate, no table, no endpoint — so
 * there is nothing to "mark as read" and no per-user inbox to draw from. This bell was in the design
 * with a hardcoded "9" beside it, and that number was removed precisely because nothing counted it.
 *
 * So the panel lists what the server ALREADY knows is waiting on the person reading it, and both the
 * badge and the rows come from the same payload, so the count can never disagree with the list:
 *
 *   * Admin — the attention queue (`/admin/dashboard/attention-queue`): dealer applications against
 *     their review SLA, disputes against theirs. Rows are built by the same presenter the dashboard's
 *     "Requires attention" panel uses, so the two cannot word the same item differently.
 *   * Dealer — the dealership's own dashboard payload: requests still unanswered, returns already
 *     overdue, and the pickups and returns due inside the platform's upcoming window.
 *
 * What this deliberately does NOT have, because the data does not exist: read/unread state, a
 * dismiss action, and any notification that is not something to act on now. An unread dot here would
 * be the "9" again.
 */
@Component({
  selector: 'kh-notifications',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './notifications-menu.component.html',
  imports: [RouterLink, IconComponent],
})
export class NotificationsMenuComponent {
  private readonly session = inject(SessionService);
  private readonly adminData = inject(AdminDashboardService);
  private readonly dealerData = inject(DealerConsoleService);
  private readonly router = inject(Router);

  protected readonly open = signal(false);

  private readonly isAdmin = computed(() => this.session.user()?.role === 'Admin');
  private readonly isDealer = computed(() => {
    const role = this.session.user()?.role;
    return role === 'DealerOwner' || role === 'DealerEmployee';
  });

  private readonly path = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects),
      startWith(this.router.url),
    ),
    { initialValue: this.router.url },
  );

  private readonly queue = loaded(this.adminData.attentionQueue);
  private readonly dashboard = loaded(this.dealerData.dashboard);

  constructor() {
    // Navigating away closes it, the same as the account menu: a panel left open over the next
    // screen is one the reader has to dismiss before they can do anything.
    effect(() => {
      this.path();
      this.open.set(false);
    });
  }

  /**
   * The rows, whichever side of the platform is reading.
   *
   * Ordered by urgency, not by time: what is already late comes first, because that is the order the
   * reader wants to work in.
   */
  protected readonly rows = computed<readonly NotificationRow[]>(() => {
    const now = Date.now();
    if (this.isAdmin()) return toAdminNotifications(this.queue() ?? null, now);
    if (this.isDealer()) return toDealerNotifications(this.dashboard() ?? null, now);
    return [];
  });

  /** The badge. Counted from the rows themselves, so it is never a second opinion about them. */
  protected readonly count = computed(() => this.rows().length);

  /** Whether the source behind the panel has answered yet. */
  protected readonly loading = computed(() =>
    this.isAdmin()
      ? this.adminData.attentionQueue.isLoading()
      : this.dealerData.dashboard.isLoading(),
  );

  protected readonly failed = computed(() =>
    this.isAdmin() ? !!this.adminData.attentionQueue.error() : !!this.dealerData.dashboard.error(),
  );

  /** Where "See everything" goes: the screen that owns this work. */
  protected readonly allRoute = computed(() =>
    this.isAdmin() ? '/dashboard' : '/dealer/dashboard',
  );

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
}
