import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { KeyValue } from '../../core/models/console.models';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { loaded } from '../../core/services/loaded';
import { MySecurityService, SessionSummary } from '../../core/services/my-security.service';
import { SessionService } from '../../core/services/session.service';
import { IconComponent } from '../../shared/icon/icon.component';

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
  private readonly service = inject(MySecurityService);
  private readonly session = inject(SessionService);
  private readonly ui = inject(ConsoleUiService);

  protected readonly resource = this.service.sessions;
  private readonly view = loaded(this.resource);

  protected readonly sessions = computed(() => this.view()?.sessions ?? []);
  protected readonly activeCount = computed(
    () => this.sessions().filter((session) => session.isActive).length,
  );

  /** From the server's own configuration, never a literal. */
  protected readonly accessTokenMinutes = computed(() => this.view()?.accessTokenMinutes ?? null);

  protected readonly accountRows = computed<readonly KeyValue[]>(() => {
    const user = this.session.user();
    if (!user) return [];
    return [
      { k: 'Name', v: user.fullName },
      { k: 'Email', v: user.email },
      { k: 'Role', v: user.role },
      { k: 'Email verified', v: user.isEmailVerified ? 'Yes' : 'No' },
    ];
  });

  protected readonly failure = computed(() => {
    const error = this.resource.error() as { status?: number } | undefined;
    if (!error) return null;
    return 'Your sessions could not be loaded. Nothing has been changed.';
  });

  protected changePassword(): void {
    this.ui.openAction(
      {
        icon: 'lock-key',
        tone: 'accent',
        title: 'Change your password',
        body: 'Every other session is signed out when the password changes. The one you are using now stays.',
        fields: [
          { label: 'Current password', type: 'password', placeholder: '' },
          { label: 'New password', type: 'password', placeholder: 'At least 8 characters' },
        ],
        confirm: 'Change password',
        result: { title: 'Password changed', body: '', tone: 'ok' },
      },
      async (values) => {
        await this.service.changePassword(
          values['Current password'] ?? '',
          values['New password'] ?? '',
        );
        this.service.refresh();
      },
      { title: 'Password changed', body: 'Your other sessions were signed out.' },
    );
  }

  protected revoke(session: SessionSummary): void {
    const minutes = this.accessTokenMinutes();
    this.ui.openAction(
      {
        icon: 'sign-out',
        tone: 'bad',
        danger: true,
        title: 'End this session?',
        body: `Signed in ${this.when(session.signedInAt)}${session.createdByIp ? ` from ${session.createdByIp}` : ''}. It cannot be refreshed after this.`,
        // The honest figure, from the server. "Signed out immediately" would be untrue for as long
        // as the access token it already holds has left to live.
        note: minutes
          ? `A session already in flight can keep working for up to ${minutes} minutes before it has to refresh. If this is the session you are using now, you will be signed out.`
          : 'If this is the session you are using now, you will be signed out.',
        confirm: 'End session',
        result: { title: 'Session ended', body: '', tone: 'bad' },
      },
      async () => {
        await this.service.revoke(session.familyId);
        this.service.refresh();
      },
      { title: 'Session ended', body: '' },
    );
  }

  protected reload(): void {
    this.resource.reload();
  }

  /** What the device says it is. Never parsed into a guess about a brand or an operating system. */
  protected device(session: SessionSummary): string {
    return session.userAgent?.trim() || 'Device not recorded';
  }

  protected when(iso: string): string {
    return new Date(iso).toLocaleString('en-GB', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  }
}
