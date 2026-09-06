import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { I18nService } from '../../core/i18n/i18n.service';
import { LanguageSwitchComponent } from '../../shared/language-switch/language-switch.component';
import { IconComponent } from '../../shared/icon/icon.component';

/**
 * Completes a reset from the emailed link.
 *
 * The token arrives as `?token=` because that is the URL AuthEmailComposer builds
 * (`{ClientBaseUrl}/reset-password?token=...`). It is single-use and expires, so a used or stale link
 * is an ordinary outcome the screen has to explain rather than an error state.
 */
@Component({
  selector: 'kh-reset-password',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './reset-password.component.html',
  imports: [FormsModule, RouterLink, IconComponent, LanguageSwitchComponent],
})
export class ResetPasswordComponent {
  protected readonly t = inject(I18nService).t;
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  protected readonly token = inject(ActivatedRoute).snapshot.queryParamMap.get('token') ?? '';
  protected readonly password = signal('');
  protected readonly busy = signal(false);
  protected readonly done = signal(false);
  protected readonly problem = signal<string | null>(null);

  /** Narrows an input event to the value the control holds. */
  protected fieldValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected async submit(): Promise<void> {
    if (this.busy()) return;

    const newPassword = this.password();
    // Length is not checked here. PasswordPolicy runs against the CONFIGURED minimum and its
    // refusal already names the figure; a literal 8 in the browser would state a rule the platform
    // may not be running and would silently stop matching the day it changes.

    this.busy.set(true);
    this.problem.set(null);

    try {
      const antiforgery = await firstValueFrom(
        this.http.get<{ requestToken: string }>('/bff/antiforgery'),
      );
      await firstValueFrom(
        this.http.post(
          '/api/v1/auth/reset-password',
          { token: this.token, newPassword },
          { headers: { 'X-XSRF-TOKEN': antiforgery.requestToken } },
        ),
      );
      this.done.set(true);
      setTimeout(() => void this.router.navigateByUrl('/sign-in'), 2500);
    } catch (error) {
      this.problem.set(describe(error));
    } finally {
      this.busy.set(false);
    }
  }
}

function describe(error: unknown): string {
  if (!(error instanceof HttpErrorResponse))
    return 'The service did not respond. Try again shortly.';

  const code: string | undefined = error.error?.code;
  if (code === 'auth.invalid_token') {
    return 'This link is invalid or has expired. Request a new one from the sign-in page.';
  }
  if (code === 'auth.password_policy' || error.status === 400) {
    return error.error?.title ?? 'That password does not meet the policy. Try a longer one.';
  }
  if (error.status === 429) {
    return 'Too many attempts. Wait a few minutes before trying again.';
  }
  return 'The service did not respond. Try again shortly.';
}
