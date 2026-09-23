import { HttpClient } from '@angular/common/http';
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
 */
@Component({
  selector: 'kh-register',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, IconComponent],
  templateUrl: './register.component.html',
})
export class RegisterComponent {
  protected readonly i18n = inject(I18nService);
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

  protected readonly config = this.appConfig.config;
  protected readonly minimumAge = computed(() => this.config()?.minimumRenterAge ?? null);

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

  protected missing(value: string): boolean {
    return this.attempted() && value.trim() === '';
  }

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    this.attempted.set(true);
    const needsBirthDate = this.minimumAge() !== null;
    const incomplete =
      !this.fullName().trim() || !this.email().trim() || !this.phone().trim() || !this.password() ||
      (needsBirthDate && !this.dateOfBirth());
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
        }),
      );
      this.done.set(registered);
    } catch (error) {
      this.problem.set(snapshotProblem(error));
    } finally {
      this.busy.set(false);
    }
  }
}
