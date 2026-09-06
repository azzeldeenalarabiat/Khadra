import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Tone } from '../../core/models/console.models';
import { AdminUserListItem, AdminUsersService } from '../../core/services/admin-users.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { loaded } from '../../core/services/loaded';
import { SessionService } from '../../core/services/session.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { I18nService } from '../../core/i18n/i18n.service';

/**
 * Who may administer the platform.
 *
 * One role, because there is one: `UserRole` has a single Admin member and no finer permission model
 * exists. The design draws a Support/Finance/Super picker; offering it would be a promise the system
 * cannot keep, so the screen says so instead of pretending.
 *
 * Deactivation is reversible and refused in two cases the server enforces — your own account, and the
 * last one that could undo it. Both are disabled here as well, with the reason on the button, so an
 * administrator is not invited to click something that will be refused.
 */
@Component({
  selector: 'kh-admin-users',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './admin-users.component.html',
  imports: [RouterLink, IconComponent],
})
export class AdminUsersComponent {
  protected readonly t = inject(I18nService).t;
  private readonly service = inject(AdminUsersService);
  private readonly session = inject(SessionService);
  private readonly ui = inject(ConsoleUiService);

  protected readonly resource = this.service.list;
  protected readonly admins = computed(() => loaded(this.resource)() ?? []);

  protected readonly failure = computed(() => {
    const error = this.resource.error() as { status?: number } | undefined;
    if (!error) return null;
    if (error.status === 403) return 'Administrator accounts are managed by administrators.';
    return 'The administrators could not be loaded. Nothing has been changed.';
  });

  /** How many accounts could still sign in. The last one cannot be deactivated. */
  protected readonly activeCount = computed(
    () => this.admins().filter((admin) => admin.status === 'Active').length,
  );

  protected isSelf(admin: AdminUserListItem): boolean {
    return this.session.user()?.id === admin.userId;
  }

  /** Why deactivation is unavailable, or null when it is. Restricted actions stay visible. */
  protected blockedReason(admin: AdminUserListItem): string | null {
    if (admin.status !== 'Active') return null;
    if (this.isSelf(admin)) return 'You cannot deactivate your own account.';
    if (this.activeCount() <= 1) {
      return 'This is the last active administrator. Invite another first.';
    }
    return null;
  }

  protected tone(admin: AdminUserListItem): Tone {
    if (admin.status === 'Suspended') return 'bad';
    return admin.isEmailVerified ? 'ok' : 'warn';
  }

  /**
   * "Invited" is not a stored status: it is an active account whose address has never been proved,
   * which is exactly what an unaccepted invitation looks like.
   */
  protected statusLabel(admin: AdminUserListItem): string {
    if (admin.status === 'Suspended') return 'Deactivated';
    return admin.isEmailVerified ? 'Active' : 'Invited';
  }

  protected invite(): void {
    this.ui.openAction(
      {
        icon: 'user-plus',
        tone: 'accent',
        title: this.t('adminUsers.inviteAnAdministrator'),
        body: this.t('adminUsers.theyGetAOne'),
        note: this.t('adminUsers.thereIsOneAdministrator'),
        fields: [
          { name: 'fullName', label: this.t('adminUsers.fullName'), type: 'text', placeholder: this.t('adminUsers.eGYousefBarakat') },
          { name: 'email', label: 'Email', type: 'text', placeholder: 'name@khadra.jo' },
          { name: 'phone', label: 'Phone', type: 'text', placeholder: '07XXXXXXXX' },
        ],
        confirm: this.t('adminUsers.sendInvitation'),
        result: { title: this.t('adminUsers.invitationSent'), body: '', tone: 'ok' },
      },
      async (values) => {
        const invited = await this.service.invite(
          values['email'] ?? '',
          values['phone'] ?? '',
          values['fullName'] ?? '',
        );
        this.service.refresh();
        // The expiry is the token's, not a literal: the lifetime is configuration.
        this.ui.showToast(
          'Invitation sent',
          `${invited.email} can accept until ${new Date(invited.expiresAt).toLocaleString('en-GB')}.`,
        );
      },
      { title: this.t('adminUsers.invitationSent'), body: '' },
    );
  }

  protected deactivate(admin: AdminUserListItem): void {
    this.ui.openAction(
      {
        icon: 'user-minus',
        tone: 'bad',
        danger: true,
        title: `Deactivate ${admin.fullName}?`,
        body: this.t('adminUsers.theyAreSignedOut'),
        note: this.t('adminUsers.reversibleTheAccountIs'),
        fields: [
          { name: 'reason', label: 'Reason', type: 'text', placeholder: this.t('adminUsers.whyIsThisAccount') },
        ],
        confirm: 'Deactivate',
        result: { title: this.t('adminUsers.administratorDeactivated'), body: '', tone: 'bad' },
      },
      async (values) => {
        await this.service.deactivate(admin.userId, values['reason'] ?? '');
        this.service.refresh();
      },
      { title: this.t('adminUsers.administratorDeactivated'), body: this.t('adminUsers.theirSessionsEndedImmediately') },
    );
  }

  protected reactivate(admin: AdminUserListItem): void {
    this.ui.openAction(
      {
        icon: 'check-circle',
        tone: 'ok',
        title: `Reactivate ${admin.fullName}?`,
        body: this.t('adminUsers.theyCanSignIn'),
        confirm: 'Reactivate',
        result: { title: this.t('adminUsers.administratorReactivated'), body: '', tone: 'ok' },
      },
      async () => {
        await this.service.reactivate(admin.userId);
        this.service.refresh();
      },
      { title: this.t('adminUsers.administratorReactivated'), body: '' },
    );
  }

  protected reload(): void {
    this.resource.reload();
  }

  protected initials(name: string): string {
    return name
      .split(' ')
      .slice(0, 2)
      .map((part) => part[0] ?? '')
      .join('');
  }

  protected when(iso: string | null): string {
    return iso
      ? new Date(iso).toLocaleDateString('en-GB', {
          day: '2-digit',
          month: 'short',
          year: 'numeric',
        })
      : 'Never';
  }
}
