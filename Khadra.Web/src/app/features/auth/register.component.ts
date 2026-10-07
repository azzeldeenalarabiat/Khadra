import { HttpClient } from '@angular/common/http';
import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AppConfigService } from '../../core/config/app-config.service';
import { ProblemSnapshot, snapshotProblem } from '../../core/http/problem';
import { problemText } from '../../core/http/problem-text';
import { I18nService } from '../../core/i18n/i18n.service';
import { instantToWallClock } from '../../core/i18n/zoned-time';
import { SeoService } from '../../core/seo/seo.service';
import { safeReturnUrl } from '../../core/session/auth.guards';
import { CONSENT_REQUIRED, VERSION_NOT_CURRENT } from '../../core/session/consent-gate.service';
import { rememberReturnAddress } from '../../core/session/return-address';
import { consentSentence, slugsInForce } from '../legal/consent-sentence';
import { IconComponent } from '../../shared/icon/icon.component';

interface Registered {
  readonly email: string;
  readonly verificationEmailSent: boolean;
}

/**
 * A customer account — the same account the app creates, through the same endpoint. Only the fields
 * the API asks for: date of birth only when the owner has set a minimum renter age (the API refuses a
 * missing one then, and does not want one otherwise), and the password rules as `/app-config`
 * publishes them. The API is the judge of every rule; this form only helps the customer meet them.
 *
 * While a legal text is in force, the customer accepts it here, and the account and the acceptance are saved together
 * (Wave 4, W4-8): the server refuses a website registration without it.
 */
@Component({
  selector: 'kh-register',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, IconComponent],
  templateUrl: './register.component.html',
})
export class RegisterComponent {
  protected readonly i18n = inject(I18nService);
  private readonly document = inject(DOCUMENT);
  private readonly appConfig = inject(AppConfigService);
  private readonly http = inject(HttpClient);

  private readonly query = toSignal(inject(ActivatedRoute).queryParamMap);
  protected readonly returnUrl = computed(() => safeReturnUrl(this.query()?.get('returnUrl'), this.i18n.language()));

  protected readonly fullName = signal('');
  protected readonly email = signal('');
  protected readonly phone = signal('');
  protected readonly password = signal('');
  protected readonly confirm = signal('');
  protected readonly dateOfBirth = signal('');
  protected readonly foreign = signal(false);
  protected readonly reveal = signal(false);
  protected readonly busy = signal(false);
  protected readonly problem = signal<ProblemSnapshot | null>(null);
  protected readonly attempted = signal(false);
  protected readonly done = signal<Registered | null>(null);
  /** The legal texts in force, accepted. Asked only while one is in force. */
  protected readonly agreed = signal(false);

  protected readonly config = this.appConfig.config;
  protected readonly minimumAge = computed(() => this.config()?.minimumRenterAge ?? null);

  /** The texts in force, as `/app-config` names them; empty while none is published, or the API cannot say. */
  private readonly legalTexts = computed(() => this.config()?.legal?.documents ?? []);
  /** The sentence beside the checkbox, each text linked to its page here, opened in a new tab. */
  protected readonly consentParts = computed(() =>
    consentSentence(slugsInForce(this.legalTexts()), this.i18n.t.bind(this.i18n)),
  );
  protected readonly consentMissing = computed(
    () => this.attempted() && this.legalTexts().length > 0 && !this.agreed(),
  );

  /** The latest date of birth the age rule allows, as a date-picker bound (the server still decides). */
  protected readonly latestBirthDate = computed(() => {
    const config = this.config();
    const age = config?.minimumRenterAge;
    if (!config || age == null) return '';
    const today = instantToWallClock(new Date(), config.timeZone).date;
    return `${Number(today.slice(0, 4)) - age}${today.slice(4)}`;
  });

  protected readonly passwordHint = computed(() => {
    const policy = this.config()?.password;
    if (!policy) return '';
    return policy.requiresLetter || policy.requiresDigit
      ? this.i18n.t('auth.passwordRules', { count: policy.minimumLength })
      : this.i18n.t('auth.passwordRulesLength', { count: policy.minimumLength });
  });

  protected readonly mismatch = computed(() => this.confirm() !== '' && this.confirm() !== this.password());
  protected readonly message = computed(() => {
    const problem = this.problem();
    return problem ? problemText(problem, this.i18n.t.bind(this.i18n), this.i18n.language(), this.config()) : null;
  });

  constructor() {
    inject(SeoService).set({ title: this.i18n.t('seo.register.title'), noindex: true });
  }

  protected checked(event: Event): boolean {
    return (event.target as HTMLInputElement).checked;
  }

  protected missing(value: string): boolean {
    return this.attempted() && value.trim() === '';
  }

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    this.attempted.set(true);
    const needsBirthDate = this.minimumAge() !== null;
    const versions = this.legalTexts().map((text) => text.versionId);
    const incomplete =
      !this.fullName().trim() || !this.email().trim() || !this.phone().trim() || !this.password() ||
      (needsBirthDate && !this.dateOfBirth()) || (versions.length > 0 && !this.agreed());
    if (incomplete || this.mismatch() || this.busy()) return;

    this.busy.set(true);
    this.problem.set(null);
    try {
      const registered = await firstValueFrom(
        this.http.post<Registered>('/api/v1/auth/register', {
          email: this.email().trim(),
          password: this.password(),
          fullName: this.fullName().trim(),
          phone: this.phone().trim(),
          dateOfBirth: needsBirthDate ? this.dateOfBirth() : null,
          isForeignNational: this.foreign(),
          // The texts the page showed, in the language it showed them in (Wave 4, W4-8).
          ...(versions.length ? { acceptedLegalVersions: versions, legalLanguage: this.i18n.language() } : {}),
        }),
      );
      this.done.set(registered);
      // The verification email opens in a new tab with nothing of this one: remember where the visitor was going.
      if (this.returnUrl() !== `/${this.i18n.language()}`) rememberReturnAddress(this.document.defaultView?.localStorage, this.returnUrl());
    } catch (error) {
      const problem = snapshotProblem(error);
      if (problem.code === VERSION_NOT_CURRENT || problem.code === CONSENT_REQUIRED) {
        // A text was replaced while this page was open, or one is in force that this page never showed: present the
        // current texts, unticked, rather than failing.
        await this.appConfig.reloadLegal();
        this.agreed.set(false);
      }
      this.problem.set(problem);
    } finally {
      this.busy.set(false);
    }
  }
}
