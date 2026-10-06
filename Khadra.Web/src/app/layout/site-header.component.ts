import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive } from '@angular/router';
import { filter, map } from 'rxjs';
import { I18nService } from '../core/i18n/i18n.service';
import { reportLanguage } from '../core/i18n/report-language';
import { NotificationsService } from '../core/api/notifications.service';
import { SessionService } from '../core/session/session.service';
import { DismissDirective } from '../shared/dismiss/dismiss.directive';
import { IconComponent } from '../shared/icon/icon.component';
import { NotificationsPanelComponent } from './notifications-panel.component';

@Component({
  selector: 'kh-site-header',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterLinkActive, IconComponent, DismissDirective, NotificationsPanelComponent],
  templateUrl: './site-header.component.html',
})
export class SiteHeaderComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly session = inject(SessionService);
  protected readonly notifications = inject(NotificationsService);
  private readonly router = inject(Router);
  private readonly http = inject(HttpClient);

  protected readonly menuOpen = signal(false);
  protected readonly accountOpen = signal(false);
  protected readonly notificationsOpen = signal(false);

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );

  /** The same page in the other language — a real link, so it works without script and gets crawled. */
  protected readonly otherLanguageUrl = computed(() =>
    I18nService.switchedUrl(this.url(), this.i18n.isArabic() ? 'en' : 'ar'),
  );

  constructor() {
    // A navigation closes whatever was open, the way a new page would.
    this.router.events.pipe(filter((event) => event instanceof NavigationEnd)).subscribe(() => {
      this.menuOpen.set(false);
      this.accountOpen.set(false);
      this.notificationsOpen.set(false);
    });
  }

  /** The bell is a toggle. Only one header panel is open at a time. */
  protected toggleNotifications(): void {
    this.accountOpen.set(false);
    this.menuOpen.set(false);
    this.notificationsOpen.update((open) => !open);
  }

  protected toggleAccount(): void {
    this.notificationsOpen.set(false);
    this.accountOpen.update((open) => !open);
  }

  /** Esc hands focus back to the button that opened the panel; a press elsewhere leaves it where it went. */
  protected closeNotifications(reason: 'outside' | 'escape', trigger: HTMLElement): void {
    this.notificationsOpen.set(false);
    if (reason === 'escape') trigger.focus();
  }

  protected closeAccount(reason: 'outside' | 'escape', trigger: HTMLElement): void {
    this.accountOpen.set(false);
    if (reason === 'escape') trigger.focus();
  }

  protected switchLanguage(event: MouseEvent): void {
    if (event.ctrlKey || event.metaKey || event.shiftKey || event.button !== 0) return;
    event.preventDefault();
    // A switch is a choice: a signed-in customer's emails and pushes follow it (Wave 3, E3).
    if (this.session.isSignedIn()) void reportLanguage(this.http, this.i18n.isArabic() ? 'en' : 'ar');
    void this.router.navigateByUrl(this.otherLanguageUrl());
  }

  protected async signOut(): Promise<void> {
    await this.session.signOut();
    void this.router.navigate(this.i18n.link());
  }
}
