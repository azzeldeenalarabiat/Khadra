import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DealerConsoleService } from '../../core/services/dealer-console.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { SessionService } from '../../core/services/session.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/en';
import { Language } from '../../core/i18n/language';
import {
  ProblemSnapshot,
  fieldMessage,
  serverSentence,
  snapshotProblem,
} from '../../core/i18n/problem';

/** What the design draws and the platform does not have yet. Keys, so a language switch re-words them. */
const NOT_LIVE: readonly { readonly title: TranslationKey; readonly body: TranslationKey }[] = [
  { title: 'dealerSettings.twoFactorAuthentication', body: 'dealerSettings.notLiveYetSign' },
  {
    title: 'employeeSettings.notificationPreferences',
    body: 'dealerSettings.notLiveYetInvitations',
  },
  { title: 'dealerSettings.bankDetailsForPayouts', body: 'dealerSettings.notLiveYetPayouts' },
  { title: 'dealerSettings.pauseOrCloseThe', body: 'dealerSettings.notLiveYetHide' },
];

/** The fields the API validates by name. A refusal of either is shown under that field. */
const PASSWORD_FIELDS = ['currentPassword', 'newPassword'] as const;
type PasswordField = (typeof PASSWORD_FIELDS)[number];

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
  private readonly i18n = inject(I18nService);
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
  /** What the last failed change said, as facts; `problemText` and `fieldError` choose the words. */
  protected readonly problem = signal<ProblemSnapshot | null>(null);

  protected readonly problemText = computed(() => {
    const p = this.problem();
    return p ? describe(p, this.t, this.i18n.lang()) : null;
  });

  protected readonly role = computed(() =>
    this.user()?.role === 'DealerOwner' ? this.t('role.dealerOwner') : this.t('employeeSettings.dealerEmployee'),
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

  protected readonly notLive = computed(() =>
    NOT_LIVE.map((item) => ({ title: this.t(item.title), body: this.t(item.body) })),
  );

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  /** The server's message for one field, in the reader's language; null when that field is fine. */
  protected fieldError(field: PasswordField): string | null {
    return fieldMessage(this.problem(), field, this.i18n.lang(), this.t);
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
      this.ui.showToast(this.t('auth.reset.doneTitle'), this.t('employeeSettings.everyOtherSessionHas'));
    } catch (error) {
      this.problem.set(snapshotProblem(error));
    } finally {
      this.busy.set(false);
    }
  }
}

/**
 * Words a refused password change for the banner, at render time. Null when the only refusal is a
 * field's own, which `fieldError` shows under that field instead.
 */
function describe(p: ProblemSnapshot, t: I18nService['t'], language: Language): string | null {
  switch (p.code) {
    case 'auth.invalid_credentials':
      return t('employeeSettings.theCurrentPasswordIs');
    case 'auth.password_unchanged':
      return t('employeeSettings.passwordUnchanged');
    case 'auth.password_policy':
      // The server's sentence names the rule that failed, with the configured length, which the
      // console is not sent. English shows it; Arabic says what was refused instead of English.
      return language === 'en' && p.title ? p.title : t('employeeSettings.passwordRulesNotMet');
  }
  if (PASSWORD_FIELDS.some((field) => fieldMessage(p, field, language, t) !== null)) return null;
  return serverSentence(p, language, t) ?? t('dealerDelivery.serviceDidNotRespond');
}
