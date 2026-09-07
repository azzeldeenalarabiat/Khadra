import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { homeRouteFor } from '../../core/guards/role.guards';
import { SessionService, SignInFailure } from '../../core/services/session.service';
import { TranslationKey } from '../../core/i18n/en';
import { I18nService } from '../../core/i18n/i18n.service';
import { MessageParams } from '../../core/i18n/language';
import { LanguageSwitchComponent } from '../../shared/language-switch/language-switch.component';
import { IconComponent } from '../../shared/icon/icon.component';

/**
 * A refusal, held as KEYS rather than as words.
 *
 * The banner outlives the click that produced it: someone can fail to sign in and then reach for
 * the language switch precisely because they could not read what went wrong. Storing the finished
 * sentence would leave that banner in the language they just rejected.
 */
interface Notice {
  readonly tone: 's-bad' | 's-warn';
  readonly titleKey: TranslationKey;
  readonly textKey: TranslationKey;
  readonly textParams?: MessageParams;
  /** Where the reader can go to fix it, when there is somewhere. */
  readonly action?: { readonly labelKey: TranslationKey; readonly route: string };
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
  imports: [FormsModule, RouterLink, IconComponent, LanguageSwitchComponent],
})
export class SignInComponent {
  protected readonly t = inject(I18nService).t;
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
        titleKey: 'auth.signIn.needBoth.title',
        textKey: 'auth.signIn.needBoth.text',
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
        titleKey: 'auth.signIn.invalid.title',
        textKey: 'auth.signIn.invalid.text',
      };

    case 'suspended':
      return {
        tone: 's-bad',
        titleKey: 'auth.signIn.suspended.title',
        textKey: 'auth.signIn.suspended.text',
      };

    case 'email-not-verified':
      return {
        tone: 's-warn',
        titleKey: 'auth.signIn.unverified.title',
        textKey: 'auth.signIn.unverified.text',
        // The link may have expired or never arrived, and this is the only screen that can send
        // another: no session exists to reach it any other way.
        action: { labelKey: 'auth.signIn.unverified.action', route: '/verify-email' },
      };

    case 'rate-limited':
      return {
        tone: 's-warn',
        titleKey: 'auth.signIn.rateLimited.title',
        ...retryMessage(failure.retryAfterSeconds),
      };

    default:
      return {
        tone: 's-bad',
        titleKey: 'auth.signIn.unavailable.title',
        textKey: 'auth.signIn.unavailable.text',
      };
  }
}

/**
 * The server does not currently send Retry-After on a 429, so the honest fallback is vague rather
 * than a number invented in the browser: someone who hit the limit fourteen minutes ago would be
 * told to wait another fifteen. If the header appears, this says exactly how long.
 */
function retryMessage(
  retryAfterSeconds: number | null,
): { textKey: TranslationKey; textParams?: MessageParams } {
  if (retryAfterSeconds === null) {
    return { textKey: 'auth.signIn.rateLimited.vague' };
  }

  const minutes = Math.ceil(retryAfterSeconds / 60);
  return { textKey: 'auth.signIn.rateLimited.minutes', textParams: { count: minutes } };
}
