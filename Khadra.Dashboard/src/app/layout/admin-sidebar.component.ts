import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { SessionService } from '../core/services/session.service';
import { DEALER_NAV, NAV_GROUPS } from '../core/data/nav.data';
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

  // Spec 1.5: one dashboard, role-based views. Dealer staff get their own business, not the platform.
  protected readonly groups = computed(() => {
    const role = this.session.user()?.role;
    return role === 'DealerOwner' || role === 'DealerEmployee' ? DEALER_NAV : NAV_GROUPS;
  });

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
