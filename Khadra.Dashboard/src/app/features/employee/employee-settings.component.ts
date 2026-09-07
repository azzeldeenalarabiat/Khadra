import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Tone } from '../../core/models/console.models';
import { DealerConsoleService } from '../../core/services/dealer-console.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { SessionService } from '../../core/services/session.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { I18nService } from '../../core/i18n/i18n.service';

type Tab = 'profile' | 'security' | 'notifications';

interface Grant {
  readonly label: string;
  readonly detail: string;
  readonly held: boolean;
  readonly tone: Tone;
}

/**
 * The employee's own account (design: Employee Console, `isSettings`).
 *
 * What is real: who the platform thinks you are, what your owner has granted you, and changing your
 * own password — which signs out every other session, by design.
 *
 * The design draws Full name, Work email and Phone as editable fields. They are shown, and they are
 * NOT editable, because no endpoint accepts them: `MySecurityController` lists and revokes sessions
 * and changes a password, and nothing else answers for your own record. Drawing a Save button over
 * that would be a promise the platform cannot keep — and email in particular is not a cosmetic
 * field, being both the sign-in identifier and where a password reset is sent, so it is not
 * something to make editable casually.
 */
@Component({
  selector: 'kh-employee-settings',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './employee-settings.component.html',
  imports: [IconComponent],
})
export class EmployeeSettingsComponent {
  protected readonly t = inject(I18nService).t;
  private readonly session = inject(SessionService);
  private readonly console = inject(DealerConsoleService);
  private readonly ui = inject(ConsoleUiService);

  protected readonly user = this.session.user;
  protected readonly dealer = loaded(this.console.me);

  protected readonly tab = signal<Tab>('profile');
  protected readonly tabs: readonly { key: Tab; label: string }[] = [
    { key: 'profile', label: 'Profile' },
    { key: 'security', label: 'Security' },
    { key: 'notifications', label: 'Notifications' },
  ];

  protected readonly current = signal('');
  protected readonly next = signal('');
  protected readonly confirm = signal('');
  protected readonly busy = signal(false);
  protected readonly problem = signal<string | null>(null);

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

  /** The same answers the rail shows, with a sentence each about what they mean in practice. */
  protected readonly grants = computed<readonly Grant[] | null>(() => {
    const permissions = this.console.permissions();
    if (!permissions) return null;

    return [
      {
        label: this.t('sidebar.permBookings'),
        detail: permissions.canDecideBookings
          ? 'Approve and reject requests, and record pickups and returns.'
          : 'Your dealership cannot take new bookings just now, so approving and rejecting are paused. Returns can still be recorded.',
        held: permissions.canDecideBookings,
        tone: permissions.canDecideBookings ? 'ok' : 'warn',
      },
      {
        label: this.t('employeeSettings.fleetAccess'),
        detail: this.t('employeeSettings.seeEveryCarIts'),
        held: true,
        tone: 'ok',
      },
      {
        label: this.t('sidebar.permReports'),
        detail: permissions.canViewReports
          ? 'Revenue, commission and occupancy are visible to you.'
          : 'Revenue, commission and occupancy are hidden. Your owner can turn this on.',
        held: permissions.canViewReports,
        tone: permissions.canViewReports ? 'ok' : 'dim',
      },
    ];
  });

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
