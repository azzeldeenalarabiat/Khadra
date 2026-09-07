import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { TranslationKey } from '../../core/i18n/en';
import { I18nService } from '../../core/i18n/i18n.service';
import { LanguageSwitchComponent } from '../../shared/language-switch/language-switch.component';
import { IconComponent } from '../../shared/icon/icon.component';

/**
 * Requests a password-reset link.
 *
 * The screen deliberately says the same thing whether or not the address is on file. The API answers
 * 202 for an unknown address exactly as for a known one, so nobody can use this form to discover
 * which emails have accounts, and the UI would give that away again if it distinguished them.
 *
 * The one outcome it DOES distinguish is a refused send: 503 `auth.password_reset_email_not_sent`.
 * This screen used to promise "a reset link is on its way" whatever the mail server had said, which
 * sent people to wait at an inbox nothing was coming to. The narrow leak that creates is accepted
 * server-side and recorded on the pre-launch checklist; it opens only while mail is broken.
 */
@Component({
  selector: 'kh-forgot-password',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './forgot-password.component.html',
  imports: [FormsModule, RouterLink, IconComponent, LanguageSwitchComponent],
})
export class ForgotPasswordComponent {
  protected readonly t = inject(I18nService).t;
  private readonly http = inject(HttpClient);

  protected readonly email = signal('');
  protected readonly busy = signal(false);
  protected readonly sent = signal(false);
  protected readonly problem = signal<string | null>(null);

  /** Narrows an input event to the value the control holds. */
  protected fieldValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected async submit(): Promise<void> {
    const email = this.email().trim();
    if (this.busy() || !email) {
      this.problem.set(email ? null : this.t('auth.forgot.needEmail'));
      return;
    }

    this.busy.set(true);
    this.problem.set(null);

    try {
      const token = await firstValueFrom(
        this.http.get<{ requestToken: string }>('/bff/antiforgery'),
      );
      await firstValueFrom(
        this.http.post(
          '/api/v1/auth/forgot-password',
          { email },
          { headers: { 'X-XSRF-TOKEN': token.requestToken } },
        ),
      );
      this.sent.set(true);
    } catch (error) {
      this.problem.set(describe(error, this.t));
    } finally {
      this.busy.set(false);
    }
  }
}

type Translate = (key: TranslationKey) => string;

function describe(error: unknown, t: Translate): string {
  if (!(error instanceof HttpErrorResponse)) {
    return t('common.noResponse');
  }
  if (error.error?.code === 'auth.password_reset_email_not_sent') {
    // Said plainly, because the alternative is the person refreshing their inbox for an hour.
    return t('auth.forgot.notSent');
  }
  if (error.status === 429) {
    return t('common.tooManyAttempts');
  }
  return t('common.noResponse');
}
