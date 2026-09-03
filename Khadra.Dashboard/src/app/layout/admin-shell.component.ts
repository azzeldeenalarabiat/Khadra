import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { AdminSidebarComponent } from './admin-sidebar.component';
import { AdminTopbarComponent } from './admin-topbar.component';
import { ConfirmModalComponent } from '../shared/confirm-modal/confirm-modal.component';
import { ToastComponent } from '../shared/toast/toast.component';

/**
 * The console frame. Sidebar and topbar are fixed; only the page area scrolls.
 * The dialog and toast hosts live here so any screen can raise one.
 */
@Component({
  selector: 'kh-admin-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './admin-shell.component.html',
  imports: [RouterOutlet, AdminSidebarComponent, AdminTopbarComponent, ConfirmModalComponent, ToastComponent],
})
export class AdminShellComponent {}
