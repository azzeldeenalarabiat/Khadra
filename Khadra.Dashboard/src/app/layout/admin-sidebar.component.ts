import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { SessionService } from '../core/services/session.service';
import { AdminDashboardService } from '../core/services/admin-dashboard.service';
import { loaded } from '../core/services/loaded';
import { DEALER_NAV, EMPLOYEE_NAV, NAV_GROUPS } from '../core/data/nav.data';
import { NavCount, NavGroup, NavRequirement, Tone } from '../core/models/console.models';
import { DealerConsoleService, DealerPermissions } from '../core/services/dealer-console.service';
import { NotificationsService } from '../core/services/notifications.service';
import { accountRouteFor } from '../core/guards/role.guards';
import { initialsOf, roleLabel } from '../core/models/user-display';
import { TranslationKey } from '../core/i18n/en';
import { I18nService } from '../core/i18n/i18n.service';
import { IconComponent } from '../shared/icon/icon.component';

/** One line of the employee's permissions panel (design: Employee Console). */
interface Grant {
  readonly labelKey: TranslationKey;
  readonly held: boolean;
  readonly tone: Tone;
}

/** Fixed navigation rail. Mirrors the AdminSidebar design component. */
@Component({
  selector: 'kh-admin-sidebar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './admin-sidebar.component.html',
  imports: [RouterLink, RouterLinkActive, IconComponent],
})
export class AdminSidebarComponent {
  protected readonly t = inject(I18nService).t;
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly dashboard = inject(AdminDashboardService);
  private readonly console = inject(DealerConsoleService);
  private readonly notifications = inject(NotificationsService);
  /** Guarded: `value()` throws in the error state, and this renders on every screen. */
  private readonly dealer = loaded(this.console.me);

  // Spec 1.5: one dashboard, role-based views. Dealer staff get their own business, not the platform.
  protected readonly isDealer = computed(() => {
    const role = this.session.user()?.role;
    return role === 'DealerOwner' || role === 'DealerEmployee';
  });

  /** An employee has a console of their own, not the owner's with pieces taken out. */
  protected readonly isEmployee = computed(() => this.session.user()?.role === 'DealerEmployee');

  protected readonly caption = computed(() => {
    const role = this.session.user()?.role;
    if (role === 'DealerEmployee') return this.t('sidebar.captionEmployee');
    return role === 'DealerOwner' ? this.t('sidebar.captionDealer') : null;
  });

  /**
   * What this member of staff holds, in the API's own terms.
   *
   * Null until `GET /dealers/me` answers. A panel that claims "financial reports: no" before it has
   * asked is worse than a gap — someone would read it and stop looking.
   *
   * "Fleet access" is read-only for an employee by design (every vehicle write is `ApprovedDealer`),
   * so it says so rather than implying they can change cars.
   */
  protected readonly grants = computed<readonly Grant[] | null>(() => {
    const permissions = this.console.permissions();
    if (!permissions) return null;

    return [
      {
        labelKey: permissions.canDecideBookings
          ? 'sidebar.permBookings'
          : 'sidebar.permBookingsPaused',
        held: permissions.canDecideBookings,
        tone: permissions.canDecideBookings ? 'ok' : 'warn',
      },
      { labelKey: 'sidebar.permFleet', held: true, tone: 'ok' },
      {
        labelKey: 'sidebar.permReports',
        held: permissions.canViewReports,
        tone: permissions.canViewReports ? 'ok' : 'dim',
      },
    ];
  });

  /**
   * The rail this member of staff can actually use.
   *
   * An Admin's list is fixed. A dealer's is filtered by what the server says this person may do:
   * Employees is owner-only (the whole controller, GET included) and Reports is a grant the owner
   * flips per person (spec 4.2), so neither can be decided from the role on the session cookie.
   *
   * Until `GET /dealers/me` answers, the gated items are OMITTED rather than guessed. Showing them
   * and taking them away a beat later is the worse of the two mistakes, and nobody is reading the
   * rail in that window anyway — the gate is showing its own skeleton over the whole console. A
   * later reload never flickers them, because a resource keeps its value while it is reloading.
   *
   * A group left with no items disappears with them; an empty "Team" heading is a promise of a
   * screen that is not there.
   */
  protected readonly groups = computed<readonly NavGroup[]>(() => {
    if (this.isEmployee()) return EMPLOYEE_NAV;
    if (!this.isDealer()) return NAV_GROUPS;

    const permissions = this.console.permissions();
    return DEALER_NAV.map((group) => ({
      ...group,
      items: group.items.filter((item) => this.holds(item.requires, permissions)),
    })).filter((group) => group.items.length > 0);
  });

  private holds(
    requires: NavRequirement | undefined,
    permissions: DealerPermissions | null,
  ): boolean {
    if (!requires) return true;
    if (!permissions) return false;
    return requires === 'manage-staff' ? permissions.canManageStaff : permissions.canViewReports;
  }

  // Both sides share this rail, so the account link has to answer for whichever side is reading it.
  protected readonly accountRoute = computed(() => accountRouteFor(this.session.user() ?? null));

  /**
   * A badge is a live count or it is nothing.
   *
   * It reads `GET /admin/workload` — two counts, re-fetched on every navigation and after every
   * decision. It used to read the whole dashboard snapshot, which was fetched once when the shell
   * loaded and never again, so a badge went on advertising an application an admin had already
   * approved for the rest of the session. Cheap enough to ask often is the whole point of that
   * endpoint being small.
   *
   * Nothing is shown until the answer arrives: a zero would claim the queue is empty when the truth
   * is that nobody has looked yet. Nothing is shown when the answer never arrives either — and it
   * has to be `loaded()` rather than `value()` for that, because `value()` throws on a failed
   * request. This runs from the shell template on EVERY admin screen, so the throw took change
   * detection down with it and froze the whole console on its loading skeleton: no error anywhere,
   * and each screen's own "couldn't load this" block never got the chance to render.
   */
  private readonly workload = loaded(this.dashboard.workload);

  protected badge(count: NavCount | undefined): string | null {
    if (!count) return null;

    // The unread count comes with the notifications feed itself, so the badge and the list are
    // reading the same payload and cannot disagree. Null until it answers: a zero would claim an
    // empty inbox when the truth is that nobody has asked yet.
    if (count === 'notifications-unread') {
      const unread = this.notifications.unreadCount();
      return unread && unread > 0 ? String(unread) : null;
    }

    const workload = this.workload();
    if (!workload) return null;

    const value =
      count === 'dealers-pending'
        ? workload.dealerApplicationsAwaitingReview
        : workload.liveDisputes;
    return value > 0 ? String(value) : null;
  }

  // The account footer names whoever is actually signed in. It used to carry the design's sample
  // admin, which told a dealer they were a Super Admin — the one label on screen that has to be true.
  protected readonly name = computed(() => this.session.user()?.fullName ?? '');
  protected readonly role = computed(() => roleLabel(this.session.user()?.role, this.t));

  /** An employee is shown where they work; everyone else, what they are. */
  protected readonly subtitle = computed(() => {
    if (!this.isEmployee()) return this.role();
    return this.dealer()?.businessName ?? this.role();
  });
  protected readonly initials = computed(() => initialsOf(this.session.user() ?? null));

  // Ends the BFF session, so the cookie is gone and the guard sends the next navigation to sign-in.
  protected async signOut(): Promise<void> {
    await this.session.signOut();
    await this.router.navigateByUrl('/sign-in');
  }
}
