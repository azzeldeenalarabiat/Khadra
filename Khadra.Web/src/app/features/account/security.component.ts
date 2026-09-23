import { HttpClient, httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { AccountSession, AccountSessions } from '../../core/api/account.api';
import { AppConfigService } from '../../core/config/app-config.service';
import { XsrfService } from '../../core/http/xsrf.service';
import { ProblemSnapshot, snapshotProblem } from '../../core/http/problem';
import { problemText } from '../../core/http/problem-text';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { StatePanelComponent } from '../../shared/state/state-panel.component';

/**
 * The password, and every phone and browser signed in with this account — the app's sessions
 * included, since it is one account. Changing the password goes through the BFF, which re-signs this
 * browser with the new tokens; the API signs every other session out.
 */
@Component({
  selector: 'kh-security',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, IconComponent, StatePanelComponent],
  templateUrl: './security.component.html',
})
export class SecurityComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly format = inject(FormatService);
  private readonly http = inject(HttpClient);
  private readonly xsrf = inject(XsrfService);
  private readonly appConfig = inject(AppConfigService);

  protected readonly sessions = httpResource<AccountSessions>(() => '/api/v1/auth/sessions');
  protected readonly sessionsProblem = computed(() => (this.sessions.error() ? snapshotProblem(this.sessions.error()) : null));
  protected readonly active = computed(() =>
    (this.sessions.value()?.sessions ?? [])
      .filter((session) => session.isActive)
      .sort((a, b) => Number(b.isCurrent) - Number(a.isCurrent) || b.signedInAt.localeCompare(a.signedInAt)),
  );

  protected readonly current = signal('');
  protected readonly next = signal('');
  protected readonly confirm = signal('');
  protected readonly busy = signal(false);
  protected readonly changed = signal(false);
  protected readonly problem = signal<ProblemSnapshot | null>(null);
  protected readonly revoking = signal<string | null>(null);
  protected readonly revoked = signal(false);

  protected readonly mismatch = computed(() => this.confirm() !== '' && this.confirm() !== this.next());
  protected readonly passwordHint = computed(() => {
    const policy = this.appConfig.config()?.password;
    if (!policy) return '';
    return policy.requiresLetter || policy.requiresDigit
      ? this.i18n.t('auth.passwordRules', { count: policy.minimumLength })
      : this.i18n.t('auth.passwordRulesLength', { count: policy.minimumLength });
  });
  protected readonly message = computed(() => {
    const problem = this.problem();
    return problem ? problemText(problem, this.i18n.t.bind(this.i18n), this.i18n.language(), this.appConfig.config()) : null;
  });

  protected device(session: AccountSession): string {
    const agent = session.userAgent ?? '';
    if (!agent) return this.i18n.t('account.unknownDevice');
    // A short, recognisable name from the user agent. Nothing is inferred beyond what it says.
    const app = /^Khadra \(/.test(agent) ? 'Khadra app' : null;
    const browser = /Edg\//.test(agent) ? 'Edge' : /Chrome\//.test(agent) ? 'Chrome' : /Firefox\//.test(agent) ? 'Firefox' : /Safari\//.test(agent) ? 'Safari' : null;
    const os = /Android/.test(agent) ? 'Android' : /iPhone|iPad/.test(agent) ? 'iOS' : /Windows/.test(agent) ? 'Windows' : /Mac OS X/.test(agent) ? 'macOS' : /Linux/.test(agent) ? 'Linux' : null;
    return [app ?? browser, os].filter(Boolean).join(' · ') || agent.slice(0, 60);
  }

  protected async changePassword(event: Event): Promise<void> {
    event.preventDefault();
    if (this.busy() || !this.current() || !this.next() || this.mismatch()) return;
    this.busy.set(true);
    this.changed.set(false);
    this.problem.set(null);
    try {
      await firstValueFrom(this.http.post('/bff/change-password', { currentPassword: this.current(), newPassword: this.next() }));
      // The BFF re-issued the session for the new tokens; the antiforgery token is bound to it.
      this.xsrf.reset();
      this.changed.set(true);
      this.current.set('');
      this.next.set('');
      this.confirm.set('');
      this.sessions.reload();
    } catch (error) {
      this.problem.set(snapshotProblem(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async revoke(session: AccountSession): Promise<void> {
    if (this.revoking()) return;
    this.revoking.set(session.familyId);
    this.revoked.set(false);
    try {
      await firstValueFrom(this.http.post(`/api/v1/auth/sessions/${session.familyId}/revoke`, {}));
      this.revoked.set(true);
      this.sessions.reload();
    } catch (error) {
      this.problem.set(snapshotProblem(error));
    } finally {
      this.revoking.set(null);
    }
  }
}
