import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AppConfigService } from '../../core/config/app-config.service';
import { ProblemSnapshot, snapshotProblem } from '../../core/http/problem';
import { problemText } from '../../core/http/problem-text';
import { I18nService } from '../../core/i18n/i18n.service';
import { SeoService } from '../../core/seo/seo.service';
import { IconComponent } from '../../shared/icon/icon.component';

/**
 * Asks for a reset link. The answer is the same whether or not an account exists — the API says
 * nothing about which addresses are registered, and neither does this page.
 */
@Component({
  selector: 'kh-forgot-password',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, IconComponent],
  templateUrl: './forgot-password.component.html',
})
export class ForgotPasswordComponent {
  protected readonly i18n = inject(I18nService);
  private readonly http = inject(HttpClient);
  private readonly appConfig = inject(AppConfigService);

  protected readonly email = signal('');
  protected readonly busy = signal(false);
  protected readonly sent = signal(false);
  protected readonly problem = signal<ProblemSnapshot | null>(null);
  protected readonly message = computed(() => {
    const problem = this.problem();
    return problem ? problemText(problem, this.i18n.t.bind(this.i18n), this.i18n.language(), this.appConfig.config()) : null;
  });

  constructor() {
    inject(SeoService).set({ title: this.i18n.t('seo.forgot.title'), noindex: true });
  }

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    if (!this.email().trim() || this.busy()) return;
    this.busy.set(true);
    this.problem.set(null);
    try {
      await firstValueFrom(this.http.post('/api/v1/auth/forgot-password', { email: this.email().trim() }));
      this.sent.set(true);
    } catch (error) {
      this.problem.set(snapshotProblem(error));
    } finally {
      this.busy.set(false);
    }
  }
}
