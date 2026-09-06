import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { TranslationKey } from '../../core/i18n/en';
import { I18nService } from '../../core/i18n/i18n.service';
import { LanguageSwitchComponent } from '../../shared/language-switch/language-switch.component';
import { IconComponent } from '../../shared/icon/icon.component';

type State = 'no-token' | 'verifying' | 'verified' | 'failed';

/**
 * Where the verification email lands.
 *
 * `AuthEmailComposer` has always built `{ClientBaseUrl}/verify-email?token=`, and until now nothing
 * answered that path: the link fell through to the console shell, the session guard bounced it to
 * sign-in, and the account stayed unverifiable. Since `User.CanAuthenticate` refuses a sign-in while
 * the address is unproved, that one missing route was the whole reason a self-registered account
 * could never be used.
 *
 * The token is spent on load rather than behind a button. It is a POST issued by script, so a mail
 * scanner following the link cannot burn it, and asking someone who just clicked "verify my email"
 * to click "verify my email" again is ceremony.
 *
 * A failure offers a new link rather than a dead end, because the common causes — the 24-hour
 * lifetime elapsing, or a second copy of the same email being opened after the first was used — are
 * both fixed by sending another one.
 */
@Component({
  selector: 'kh-verify-email',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './verify-email.component.html',
  imports: [FormsModule, RouterLink, IconComponent, LanguageSwitchComponent],
})
export class VerifyEmailComponent {
  protected readonly t = inject(I18nService).t;
  private readonly http = inject(HttpClient);

  private readonly token = inject(ActivatedRoute).snapshot.queryParamMap.get('token') ?? '';

  protected readonly state = signal<State>(this.token ? 'verifying' : 'no-token');
  protected readonly problem = signal<string | null>(null);

  /** The resend panel: only offered once verification has actually failed. */
  protected readonly email = signal('');
  protected readonly resending = signal(false);
  protected readonly resent = signal(false);

  constructor() {
    if (this.token) void this.verify();
  }

  protected fieldValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  private async verify(): Promise<void> {
    try {
      await firstValueFrom(
        this.http.post(
          '/api/v1/auth/verify-email',
          { token: this.token },
          { headers: { 'X-XSRF-TOKEN': await this.antiforgery() } },
        ),
      );
      this.state.set('verified');
    } catch (error) {
      this.problem.set(describe(error, this.t));
      this.state.set('failed');
    }
  }

  protected async resend(): Promise<void> {
    if (this.resending()) return;
    const email = this.email().trim();
    if (!email) {
      this.problem.set(this.t('auth.verify.needEmail'));
      return;
    }

    this.resending.set(true);
    this.problem.set(null);
    try {
      await firstValueFrom(
        this.http.post(
          '/api/v1/auth/resend-verification',
          { email },
          { headers: { 'X-XSRF-TOKEN': await this.antiforgery() } },
        ),
      );
      // The server answers the same way whether or not the address is registered, and so does this
      // screen: saying "no such account" here would turn the form into an address checker. A send
      // that was attempted and REFUSED is different — the server reports that as a failure, which
      // lands in the catch below, because telling someone a link is on its way when the mail server
      // rejected it is the false confirmation this change exists to remove.
      this.resent.set(true);
    } catch (error) {
      this.problem.set(describe(error, this.t));
    } finally {
      this.resending.set(false);
    }
  }

  /** The BFF validates the antiforgery header on every proxied POST, anonymous ones included. */
  private async antiforgery(): Promise<string> {
    const issued = await firstValueFrom(
      this.http.get<{ requestToken: string }>('/bff/antiforgery'),
    );
    return issued.requestToken;
  }
}

type Translate = (key: TranslationKey) => string;

function describe(error: unknown, t: Translate): string {
  if (!(error instanceof HttpErrorResponse)) {
    return t('common.noResponse');
  }
  const code: string | undefined = error.error?.code;
  if (code === 'auth.invalid_token') {
    return t('auth.verify.err.invalidToken');
  }
  if (code === 'auth.verification_email_not_sent') {
    return t('auth.verify.err.notSent');
  }
  if (error.status === 429) {
    return t('common.tooManyAttempts');
  }
  return error.error?.title ?? t('common.noResponse');
}
