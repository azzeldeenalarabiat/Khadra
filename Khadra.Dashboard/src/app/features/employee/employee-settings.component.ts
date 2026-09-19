import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Tone } from '../../core/models/console.models';
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

type Tab = 'profile' | 'security' | 'notifications';

/** The tabs, labelled by key so a language switch re-words them. `key` is the machine value. */
const TABS: readonly { readonly key: Tab; readonly label: TranslationKey }[] = [
  { key: 'profile', label: 'employeeSettings.profile' },
  { key: 'security', label: 'employeeSettings.security' },
  { key: 'notifications', label: 'employeeSettings.notifications' },
];

/** The fields the API validates by name. A refusal of either is shown under that field. */
const PASSWORD_FIELDS = ['currentPassword', 'newPassword'] as const;
type PasswordField = (typeof PASSWORD_FIELDS)[number];

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
  private readonly i18n = inject(I18nService);
  protected readonly t = inject(I18nService).t;
  private readonly session = inject(SessionService);
  private readonly console = inject(DealerConsoleService);
  private readonly ui = inject(ConsoleUiService);

  protected readonly user = this.session.user;
  protected readonly dealer = loaded(this.console.me);

  protected readonly tab = signal<Tab>('profile');
  protected readonly tabs = computed(() =>
    TABS.map((option) => ({ key: option.key, label: this.t(option.label) })),
  );

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
          ? this.t('employeeSettings.approveAndRejectRequests')
          : this.t('employeeSettings.yourDealershipCannotTake'),
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
          ? this.t('employeeSettings.revenueCommissionAndOccupancy')
          : this.t('employeeSettings.revenueCommissionAndOccupancy2'),
        held: permissions.canViewReports,
        tone: permissions.canViewReports ? 'ok' : 'dim',
      },
    ];
  });

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
