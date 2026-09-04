import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { SessionService } from '../core/services/session.service';
import { AdminDashboardService } from '../core/services/admin-dashboard.service';
import { DEALER_NAV, NAV_GROUPS } from '../core/data/nav.data';
import { NavCount } from '../core/models/console.models';
import { initialsOf, roleLabel } from '../core/models/user-display';
import { IconComponent } from '../shared/icon/icon.component';

/** Fixed navigation rail. Mirrors the AdminSidebar design component. */
@Component({
  selector: 'kh-admin-sidebar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './admin-sidebar.component.html',
  imports: [RouterLink, RouterLinkActive, IconComponent],
})
export class AdminSidebarComponent {
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly dashboard = inject(AdminDashboardService);

  // Spec 1.5: one dashboard, role-based views. Dealer staff get their own business, not the platform.
  protected readonly isDealer = computed(() => {
    const role = this.session.user()?.role;
    return role === 'DealerOwner' || role === 'DealerEmployee';
  });

  protected readonly groups = computed(() => (this.isDealer() ? DEALER_NAV : NAV_GROUPS));

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
   * is that nobody has looked yet.
   */
  protected badge(count: NavCount | undefined): string | null {
    if (!count) return null;
    const workload = this.dashboard.workload.value();
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
  protected readonly role = computed(() => roleLabel(this.session.user()?.role));
  protected readonly initials = computed(() => initialsOf(this.session.user() ?? null));

  // Ends the BFF session, so the cookie is gone and the guard sends the next navigation to sign-in.
  protected async signOut(): Promise<void> {
    await this.session.signOut();
    await this.router.navigateByUrl('/sign-in');
  }
}
