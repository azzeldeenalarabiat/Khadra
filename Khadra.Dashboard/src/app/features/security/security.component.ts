import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { KeyValue } from '../../core/models/console.models';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { loaded } from '../../core/services/loaded';
import { MySecurityService, SessionSummary } from '../../core/services/my-security.service';
import { SessionService } from '../../core/services/session.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { I18nService } from '../../core/i18n/i18n.service';
import { FormatService } from '../../core/i18n/format.service';
import { roleLabel } from '../../core/models/user-display';

/** An account row. `ltr` marks a Latin run -- an email address -- the template isolates. */
interface AccountRow extends KeyValue {
  readonly ltr?: boolean;
}

/**
 * Your own account security.
 *
 * Your own, and nobody else's: reading another person's devices, addresses and sign-in times is
 * surveillance rather than administration, and the API has no route for it.
 *
 * The design draws a sign-in history, a second factor and a "suspicious sign-in blocked" feed. None
 * of the three exists — there is no sign-in log, no lockout and no MFA — so this screen says which
 * ones are missing rather than drawing a panel that would have to be filled with invention.
 */
@Component({
  selector: 'kh-security',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './security.component.html',
  imports: [IconComponent],
})
export class SecurityComponent {
  protected readonly t = inject(I18nService).t;
  private readonly formats = inject(FormatService);
  private readonly service = inject(MySecurityService);
  private readonly session = inject(SessionService);
  private readonly ui = inject(ConsoleUiService);

  protected readonly resource = this.service.sessions;
  private readonly view = loaded(this.resource);

  protected readonly sessions = computed(() => this.view()?.sessions ?? []);
  protected readonly activeCount = computed(
    () => this.sessions().filter((session) => session.isActive).length,
  );

  /**
   * "2 active sessions", once the server has answered. Empty until then: a "0" printed while the
   * list is still loading would say this account is signed in nowhere.
   */
  protected readonly activeSummary = computed(() =>
    this.view() ? this.t('security.activeSessionsCount', { count: this.activeCount() }) : '',
  );

  /** From the server's own configuration, never a literal. */
  protected readonly accessTokenMinutes = computed(() => this.view()?.accessTokenMinutes ?? null);

  protected readonly accountRows = computed<readonly AccountRow[]>(() => {
    const user = this.session.user();
    if (!user) return [];
    return [
      { k: this.t('dealerSettings.name'), v: user.fullName },
      { k: this.t('dealerSettings.email'), v: user.email, ltr: true },
      // The role in words; a role this build does not know is shown as the server named it.
      { k: this.t('common.role'), v: roleLabel(user.role, this.t) || user.role },
      {
        k: this.t('customerProfile.emailVerified'),
        v: user.isEmailVerified ? this.t('common.yes') : this.t('common.no'),
      },
    ];
  });

  protected readonly failure = computed(() => {
    const error = this.resource.error() as { status?: number } | undefined;
    if (!error) return null;
    return this.t('security.yourSessionsCouldNot');
  });

  protected changePassword(): void {
    this.ui.openAction(
      {
        icon: 'lock-key',
        tone: 'accent',
        title: this.t('security.changeYourPassword'),
        body: this.t('security.everyOtherSessionIs'),
        fields: [
          { name: 'currentPassword', label: this.t('common.currentPassword'), type: 'password', placeholder: '' },
          { name: 'newPassword', label: this.t('common.newPassword'), type: 'password', placeholder: this.t('security.yourNewPassword') },
        ],
        confirm: this.t('common.changePassword'),
        result: { title: this.t('auth.reset.doneTitle'), body: '', tone: 'ok' },
      },
      async (values) => {
        await this.service.changePassword(
          values['currentPassword'] ?? '',
          values['newPassword'] ?? '',
        );
        this.service.refresh();
      },
      { title: this.t('auth.reset.doneTitle'), body: this.t('security.yourOtherSessionsWere') },
    );
  }

  protected revoke(session: SessionSummary): void {
    const minutes = this.accessTokenMinutes();
    this.ui.openAction(
      {
        icon: 'sign-out',
        tone: 'bad',
        danger: true,
        title: this.t('security.endThisSession'),
        // One sentence with or without the address, so neither language glues a fragment on.
        body: session.createdByIp
          ? this.t('security.signedInFromCannotBeRefreshed', {
              date: this.when(session.signedInAt),
              address: session.createdByIp,
            })
          : this.t('security.signedInCannotBeRefreshed', { date: this.when(session.signedInAt) }),
        // The honest figure, from the server. "Signed out immediately" would be untrue for as long
        // as the access token it already holds has left to live.
        note: minutes
          ? this.t('security.inFlightForUpToMinutes', { count: minutes })
          : this.t('security.ifThisIsThe'),
        confirm: this.t('security.endSession'),
        result: { title: this.t('security.sessionEnded'), body: '', tone: 'bad' },
      },
      async () => {
        await this.service.revoke(session.familyId);
        this.service.refresh();
      },
      { title: this.t('security.sessionEnded'), body: '' },
    );
  }

  protected reload(): void {
    this.resource.reload();
  }

  /** What the device says it is. Never parsed into a guess about a brand or an operating system. */
  protected device(session: SessionSummary): string {
    return session.userAgent?.trim() || this.t('security.deviceNotRecorded');
  }

  protected when(iso: string): string {
    return this.formats.dateTime(iso);
  }
}
