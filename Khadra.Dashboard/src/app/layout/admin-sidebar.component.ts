import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { SessionService } from '../core/services/session.service';
import { NAV_GROUPS } from '../core/data/nav.data';
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

  protected readonly groups = NAV_GROUPS;

  // Ends the BFF session, so the cookie is gone and the guard sends the next navigation to sign-in.
  protected async signOut(): Promise<void> {
    await this.session.signOut();
    await this.router.navigateByUrl('/sign-in');
  }
}
