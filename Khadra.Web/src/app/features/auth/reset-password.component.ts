import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AppConfigService } from '../../core/config/app-config.service';
import { ProblemSnapshot, snapshotProblem } from '../../core/http/problem';
import { problemText } from '../../core/http/problem-text';
import { I18nService } from '../../core/i18n/i18n.service';
import { SeoService } from '../../core/seo/seo.service';
import { IconComponent } from '../../shared/icon/icon.component';

/**
 * Where a reset email's link lands. The token is read once and taken out of the address, so it is
 * not left in the history or in a screenshot of the page.
 */
@Component({
  selector: 'kh-reset-password',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, IconComponent],
  templateUrl: './reset-password.component.html',
})
export class ResetPasswordComponent {
  protected readonly i18n = inject(I18nService);
  private readonly http = inject(HttpClient);
  private readonly appConfig = inject(AppConfigService);

  private readonly token: string | null;
  protected readonly hasToken: boolean;
  protected readonly password = signal('');
  protected readonly confirm = signal('');
  protected readonly reveal = signal(false);
  protected readonly busy = signal(false);
  protected readonly done = signal(false);
  protected readonly problem = signal<ProblemSnapshot | null>(null);

  protected readonly mismatch = computed(() => this.confirm() !== '' && this.confirm() !== this.password());
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

  constructor() {
    inject(SeoService).set({ title: this.i18n.t('seo.reset.title'), noindex: true });
    this.token = inject(ActivatedRoute).snapshot.queryParamMap.get('token');
    this.hasToken = !!this.token;
    if (this.token) {
      void inject(Router).navigate([], { queryParams: { token: null }, queryParamsHandling: 'merge', replaceUrl: true });
    }
  }

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    if (!this.token || !this.password() || this.mismatch() || this.busy()) return;
    this.busy.set(true);
    this.problem.set(null);
    try {
      await firstValueFrom(this.http.post('/api/v1/auth/reset-password', { token: this.token, newPassword: this.password() }));
      this.done.set(true);
    } catch (error) {
      this.problem.set(snapshotProblem(error));
    } finally {
      this.busy.set(false);
    }
  }
}
