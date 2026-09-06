import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { homeRouteFor } from '../../core/guards/role.guards';
import { SessionService, SignInFailure } from '../../core/services/session.service';
import { IconComponent } from '../../shared/icon/icon.component';

interface Notice {
  readonly tone: 's-bad' | 's-warn';
  readonly title: string;
  readonly text: string;
  /** Where the reader can go to fix it, when there is somewhere. */
  readonly action?: { readonly label: string; readonly route: string };
}

/**
 * The console's front door.
 *
 * The one decision worth stating: a failed sign-in is described by CASE, not by status code. An
 * account that has been suspended and a mistyped password both stop you getting in, but they need
 * different things from you, and telling a suspended admin their password is wrong sends them round
 * a reset loop that cannot possibly work.
 */
@Component({
  selector: 'kh-sign-in',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './sign-in.component.html',
  imports: [FormsModule, RouterLink, IconComponent],
})
export class SignInComponent {
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly email = signal('');
  protected readonly password = signal('');
  protected readonly busy = signal(false);
  protected readonly notice = signal<Notice | null>(null);

  /** Narrows an input event to the value the control holds. */
  protected fieldValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected async submit(): Promise<void> {
    if (this.busy()) return;

    const email = this.email().trim();
    const password = this.password();
    if (!email || !password) {
      this.notice.set({
        tone: 's-warn',
        title: 'Enter your email and password',
        text: 'Both are needed to sign in.',
      });
      return;
    }

    this.busy.set(true);
    this.notice.set(null);

    const result = await this.session.signIn(email, password);
    this.busy.set(false);

    if (result.ok) {
      // Back to wherever the guard interrupted, or the dashboard.
      // Whatever the guard interrupted, or the home this role belongs on (spec 1.5).
      const returnUrl =
        this.route.snapshot.queryParamMap.get('returnUrl') ?? homeRouteFor(result.user);
      await this.router.navigateByUrl(returnUrl);
      return;
    }

    this.notice.set(describe(result.failure));
  }
}

function describe(failure: SignInFailure): Notice {
  switch (failure.kind) {
    case 'invalid-credentials':
      return {
        tone: 's-bad',
        title: 'Invalid email or password',
        text: 'Check both and try again.',
      };

    case 'suspended':
      return {
        tone: 's-bad',
        title: 'Your account has been suspended',
        text: 'Contact support to have it reviewed. Resetting your password will not restore access.',
      };

    case 'email-not-verified':
      return {
        tone: 's-warn',
        title: 'Verify your email address first',
        text: 'We sent a verification link when the account was created. Open it, then sign in.',
        // The link may have expired or never arrived, and this is the only screen that can send
        // another: no session exists to reach it any other way.
        action: { label: 'Send a new link', route: '/verify-email' },
      };

    case 'rate-limited':
      return {
        tone: 's-warn',
        title: 'Too many attempts',
        text: retryText(failure.retryAfterSeconds),
      };

    default:
      return {
        tone: 's-bad',
        title: 'Sign-in is unavailable',
        text: 'The service did not respond. Nothing about your account has changed; try again shortly.',
      };
  }
}

/**
 * The server does not currently send Retry-After on a 429, so the honest fallback is vague rather
 * than a number invented in the browser: someone who hit the limit fourteen minutes ago would be
 * told to wait another fifteen. If the header appears, this says exactly how long.
 */
function retryText(retryAfterSeconds: number | null): string {
  if (retryAfterSeconds === null) {
    return 'Too many sign-in attempts from this network. Wait a few minutes before trying again.';
  }

  const minutes = Math.ceil(retryAfterSeconds / 60);
  if (minutes <= 1) return 'Try again in about a minute.';
  return `Try again in about ${minutes} minutes.`;
}
