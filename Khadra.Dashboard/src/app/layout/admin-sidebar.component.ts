import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
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
  protected readonly groups = NAV_GROUPS;
}
