import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, afterNextRender, computed, inject, signal } from '@angular/core';
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
 * Where the link in a verification email lands. The token is single-use, so it is spent once — in the
 * browser, after the page has rendered — and then taken out of the address, so a reload or a shared
 * screenshot cannot try it again. The same page asks for a new link.
 */
@Component({
  selector: 'kh-verify-email',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, IconComponent],
  templateUrl: './verify-email.component.html',
})
export class VerifyEmailComponent {
  protected readonly i18n = inject(I18nService);
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly appConfig = inject(AppConfigService);

  protected readonly state = signal<'idle' | 'working' | 'verified' | 'failed'>('idle');
  protected readonly problem = signal<ProblemSnapshot | null>(null);
  protected readonly email = signal('');
  protected readonly resendBusy = signal(false);
  protected readonly resent = signal(false);
  protected readonly resendProblem = signal<ProblemSnapshot | null>(null);

  protected readonly resendMessage = computed(() => {
    const problem = this.resendProblem();
    return problem ? problemText(problem, this.i18n.t.bind(this.i18n), this.i18n.language(), this.appConfig.config()) : null;
  });

  constructor() {
    inject(SeoService).set({ title: this.i18n.t('seo.verify.title'), noindex: true });
    const route = inject(ActivatedRoute);
    this.email.set(route.snapshot.queryParamMap.get('email') ?? '');
    const token = route.snapshot.queryParamMap.get('token');
    if (token) afterNextRender(() => void this.verify(token));
  }

  private async verify(token: string): Promise<void> {
    this.state.set('working');
    void this.router.navigate([], { queryParams: { token: null }, queryParamsHandling: 'merge', replaceUrl: true });
    try {
      await firstValueFrom(this.http.post('/api/v1/auth/verify-email', { token }));
      this.state.set('verified');
    } catch (error) {
      this.problem.set(snapshotProblem(error));
      this.state.set('failed');
    }
  }

  protected async resend(event: Event): Promise<void> {
    event.preventDefault();
    if (!this.email().trim() || this.resendBusy()) return;
    this.resendBusy.set(true);
    this.resendProblem.set(null);
    try {
      await firstValueFrom(this.http.post('/api/v1/auth/resend-verification', { email: this.email().trim() }));
      this.resent.set(true);
    } catch (error) {
      this.resendProblem.set(snapshotProblem(error));
    } finally {
      this.resendBusy.set(false);
    }
  }
}
