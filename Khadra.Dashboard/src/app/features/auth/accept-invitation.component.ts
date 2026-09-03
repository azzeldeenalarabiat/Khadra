import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { IconComponent } from '../../shared/icon/icon.component';

/**
 * An invited employee takes up their account (spec 4.2).
 *
 * The token arrives as `?token=` because that is the link AuthEmailComposer builds. Accepting proves
 * the mailbox and sets the first password in one step; until then the account cannot sign in at all,
 * so a stale or used link is an ordinary outcome to explain, not an error state.
 */
@Component({
  selector: 'kh-accept-invitation',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './accept-invitation.component.html',
  imports: [FormsModule, RouterLink, IconComponent],
})
export class AcceptInvitationComponent {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  protected readonly token = inject(ActivatedRoute).snapshot.queryParamMap.get('token') ?? '';
  protected readonly password = signal('');
  protected readonly busy = signal(false);
  protected readonly done = signal(false);
  protected readonly problem = signal<string | null>(null);

  protected fieldValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected async submit(): Promise<void> {
    if (this.busy()) return;
    const password = this.password();
    if (password.length < 8) {
      this.problem.set('Use at least 8 characters, including a letter and a digit.');
      return;
    }

    this.busy.set(true);
    this.problem.set(null);
    try {
      const antiforgery = await firstValueFrom(
        this.http.get<{ requestToken: string }>('/bff/antiforgery'),
      );
      await firstValueFrom(
        this.http.post(
          '/api/v1/auth/accept-invitation',
          { token: this.token, password },
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
  if (!(error instanceof HttpErrorResponse)) {
    return 'The service did not respond. Try again shortly.';
  }
  const code: string | undefined = error.error?.code;
  if (code === 'auth.invalid_token') {
    return 'This invitation is no longer valid — it may have expired or already been used. Ask the dealer owner to send a new one.';
  }
  if (code === 'auth.password_policy' || error.status === 400) {
    return error.error?.title ?? 'That password does not meet the policy. Try a longer one.';
  }
  if (error.status === 429) {
    return 'Too many attempts. Wait a few minutes before trying again.';
  }
  return 'The service did not respond. Try again shortly.';
}
