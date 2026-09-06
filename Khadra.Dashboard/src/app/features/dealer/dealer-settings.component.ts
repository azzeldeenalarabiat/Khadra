import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DealerConsoleService } from '../../core/services/dealer-console.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { SessionService } from '../../core/services/session.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { I18nService } from '../../core/i18n/i18n.service';

/**
 * Settings (design `isSettings`).
 *
 * What is real: the account as the platform knows it, and changing your own password (which signs
 * out every other session, by design). What the design shows and the platform does not have --
 * two-factor, notification preferences, pausing or closing the account, bank details for payouts,
 * tax settings -- is listed as "not live" rather than drawn as working controls.
 */
@Component({
  selector: 'kh-dealer-settings',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dealer-settings.component.html',
  imports: [IconComponent],
})
export class DealerSettingsComponent {
  protected readonly t = inject(I18nService).t;
  private readonly session = inject(SessionService);
  private readonly console = inject(DealerConsoleService);
  private readonly ui = inject(ConsoleUiService);

  protected readonly user = this.session.user;
  protected readonly dealer = loaded(this.console.me);

  protected readonly current = signal('');
  protected readonly next = signal('');
  protected readonly confirm = signal('');
  protected readonly busy = signal(false);
  protected readonly problem = signal<string | null>(null);

  protected readonly role = computed(() =>
    this.user()?.role === 'DealerOwner' ? 'Dealer owner' : 'Dealer employee',
  );

  protected readonly mismatch = computed(
    () => this.confirm().length > 0 && this.next() !== this.confirm(),
  );
  protected readonly canSubmit = computed(
    () =>
      !this.busy() &&
      this.current().length > 0 &&
      this.next().length >= 8 &&
      this.next() === this.confirm(),
  );

  protected readonly notLive: readonly { readonly title: string; readonly body: string }[] = [
    { title: this.t('dealerSettings.twoFactorAuthentication'), body: this.t('dealerSettings.notLiveYetSign') },
    {
      title: this.t('employeeSettings.notificationPreferences'),
      body: this.t('dealerSettings.notLiveYetInvitations'),
    },
    {
      title: this.t('dealerSettings.bankDetailsForPayouts'),
      body: this.t('dealerSettings.notLiveYetPayouts'),
    },
    {
      title: this.t('dealerSettings.pauseOrCloseThe'),
      body: this.t('dealerSettings.notLiveYetHide'),
    },
  ];

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected async changePassword(): Promise<void> {
    if (!this.canSubmit()) return;
    this.busy.set(true);
    this.problem.set(null);
    try {
      await this.session.changePassword(this.current(), this.next());
      this.current.set('');
      this.next.set('');
      this.confirm.set('');
      this.ui.showToast('Password changed', 'Every other session has been signed out.');
    } catch (error) {
      const p = error as {
        status?: number;
        error?: { code?: string; title?: string; errors?: Record<string, string[]> };
      };
      const first = p.error?.errors ? Object.values(p.error.errors)[0]?.[0] : undefined;
      this.problem.set(
        p.error?.code === 'auth.invalid_credentials'
          ? 'The current password is wrong.'
          : (first ?? p.error?.title ?? 'The service did not respond. Nothing has been changed.'),
      );
    } finally {
      this.busy.set(false);
    }
  }
}
