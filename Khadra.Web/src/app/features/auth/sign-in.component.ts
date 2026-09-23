import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { I18nService } from '../../core/i18n/i18n.service';
import { SeoService } from '../../core/seo/seo.service';
import { safeReturnUrl } from '../../core/session/auth.guards';
import { SessionService, SignInFailure } from '../../core/session/session.service';
import { IconComponent } from '../../shared/icon/icon.component';

/**
 * Sign-in, through the customer BFF. The browser sends the password once, to `/bff/login`; the BFF
 * signs in with the API, keeps the tokens, and hands back only an HttpOnly session cookie. Only
 * customer accounts may hold a session here; a rental office or admin account is told where it signs in.
 */
@Component({
  selector: 'kh-sign-in',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, IconComponent],
  templateUrl: './sign-in.component.html',
})
export class SignInComponent {
  protected readonly i18n = inject(I18nService);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly http = inject(HttpClient);

  private readonly query = toSignal(inject(ActivatedRoute).queryParamMap);
  protected readonly returnUrl = computed(() => safeReturnUrl(this.query()?.get('returnUrl'), this.i18n.language()));
  protected readonly sessionEnded = computed(() => this.query()?.get('reason') === 'ended');

  protected readonly email = signal('');
  protected readonly password = signal('');
  protected readonly reveal = signal(false);
  protected readonly busy = signal(false);
  protected readonly failure = signal<SignInFailure | null>(null);

  protected readonly message = computed(() => {
    const failure = this.failure();
    if (!failure) return null;
    switch (failure.kind) {
      case 'invalid-credentials':
        return this.i18n.t('signIn.invalid');
      case 'suspended':
        return this.i18n.t('signIn.suspended');
      case 'email-not-verified':
        return this.i18n.t('signIn.unverified');
      case 'wrong-account-type':
        return this.i18n.t('signIn.wrongAccount');
      case 'rate-limited':
        return failure.retryAfterSeconds !== null
          ? this.i18n.t('signIn.rateLimitedFor', { count: failure.retryAfterSeconds })
          : this.i18n.t('signIn.rateLimited');
      default:
        return this.i18n.t('signIn.unavailable');
    }
  });

  constructor() {
    inject(SeoService).set({ title: this.i18n.t('seo.signIn.title'), noindex: true });
  }

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    if (this.busy() || !this.email().trim() || !this.password()) return;
    this.busy.set(true);
    this.failure.set(null);
    const result = await this.session.signIn(this.email().trim(), this.password());
    this.busy.set(false);
    if (!result.ok) {
      this.failure.set(result.failure);
      return;
    }
    // The language the customer is reading in becomes the one their emails and pushes use, as the
    // app does when a language is chosen. Best effort: a failure here must not undo the sign-in.
    void firstValueFrom(this.http.put('/api/v1/auth/me/language', { language: this.i18n.language() })).catch(() => undefined);
    void this.router.navigateByUrl(this.returnUrl());
  }
}
