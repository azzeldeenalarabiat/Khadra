import { ChangeDetectionStrategy, Component, ElementRef, inject, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
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
  imports: [
    RouterOutlet,
    AdminSidebarComponent,
    AdminTopbarComponent,
    ConfirmModalComponent,
    ToastComponent,
  ],
})
export class AdminShellComponent {
  private readonly page = viewChild<ElementRef<HTMLElement>>('page');

  constructor() {
    // The page area scrolls, not the window, so Angular's own scroll
    // restoration cannot see it. Without this, opening a record from row 40 of
    // a list drops you 40 rows down the record.
    inject(Router)
      .events.pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.page()?.nativeElement.scrollTo({ top: 0 }));
  }
}
