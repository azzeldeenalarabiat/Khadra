import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Tone } from '../../core/models/console.models';
import { AdminUserListItem, AdminUsersService } from '../../core/services/admin-users.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { loaded } from '../../core/services/loaded';
import { SessionService } from '../../core/services/session.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { I18nService } from '../../core/i18n/i18n.service';
import { FormatService } from '../../core/i18n/format.service';

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
  protected readonly formats = inject(FormatService);
  private readonly service = inject(AdminUsersService);
  private readonly session = inject(SessionService);
  private readonly ui = inject(ConsoleUiService);

  protected readonly resource = this.service.list;
  protected readonly admins = computed(() => loaded(this.resource)() ?? []);

  protected readonly failure = computed(() => {
    const error = this.resource.error() as { status?: number } | undefined;
    if (!error) return null;
    if (error.status === 403) return this.t('adminUsers.administratorAccountsAreManaged');
    return this.t('adminUsers.theAdministratorsCouldNot');
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
    if (this.isSelf(admin)) return this.t('adminUsers.youCannotDeactivateYour');
    if (this.activeCount() <= 1) {
      return this.t('adminUsers.thisIsTheLast');
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
    if (admin.status === 'Suspended') return this.t('status.deactivated');
    return admin.isEmailVerified ? this.t('status.active') : this.t('status.invited');
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
          // `name` is the key each typed value is stored under and read back by below: a machine
          // name, never a translation, or an Arabic screen sends an empty email and phone. The two
          // placeholders are format hints, the same in both languages.
          {
            name: 'fullName',
            label: this.t('adminUsers.fullName'),
            type: 'text',
            placeholder: this.t('adminUsers.eGYousefBarakat'),
          },
          {
            name: 'email',
            label: this.t('dealerSettings.email'),
            type: 'text',
            placeholder: 'name@khadra.jo',
          },
          {
            name: 'phone',
            label: this.t('customerProfile.phone'),
            type: 'text',
            placeholder: '07XXXXXXXX',
          },
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
          this.t('adminUsers.invitationSent'),
          this.t('adminUsers.canAcceptUntil', {
            email: invited.email,
            date: this.formats.dateTime(invited.expiresAt),
          }),
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
        title: this.t('adminUsers.deactivateNameQuestion', { name: admin.fullName }),
        body: this.t('adminUsers.theyAreSignedOut'),
        note: this.t('adminUsers.reversibleTheAccountIs'),
        fields: [
          {
            // Read back as values['reason']: a machine name, never a translation.
            name: 'reason',
            label: this.t('dealerDecide.reject.reasonLabel'),
            type: 'text',
            placeholder: this.t('adminUsers.whyIsThisAccount'),
          },
        ],
        confirm: this.t('common.deactivate'),
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
        title: this.t('adminUsers.reactivateNameQuestion', { name: admin.fullName }),
        body: this.t('adminUsers.theyCanSignIn'),
        confirm: this.t('common.reactivate'),
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
    return iso ? this.formats.date(iso) : this.t('common.never');
  }
}
