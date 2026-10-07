import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/en';
import { CONSENT_REQUIRED, VERSION_NOT_CURRENT } from '../../core/services/consent-gate.service';
import { PlatformConfigService } from '../../core/services/platform-config.service';
import { LanguageSwitchComponent } from '../../shared/language-switch/language-switch.component';
import { IconComponent } from '../../shared/icon/icon.component';
import { LegalConsentComponent } from '../../shared/legal-consent/legal-consent.component';
import { LegalLinksComponent } from '../../shared/legal-links/legal-links.component';

/**
 * An invited employee takes up their account (spec 4.2).
 *
 * The token arrives as `?token=` because that is the link AuthEmailComposer builds. Accepting proves
 * the mailbox and sets the first password in one step; until then the account cannot sign in at all,
 * so a stale or used link is an ordinary outcome to explain, not an error state.
 *
 * A member of a rental office's staff accepts the legal texts in force here (Wave 4, W4-8). Their link says so
 * (`for=staff`); an administrator's does not, and is never asked. A link sent before the hint existed is answered
 * by the server's refusal, which brings the checkbox up.
 */
@Component({
  selector: 'kh-accept-invitation',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './accept-invitation.component.html',
  imports: [FormsModule, RouterLink, IconComponent, LanguageSwitchComponent, LegalConsentComponent, LegalLinksComponent],
})
export class AcceptInvitationComponent {
  private readonly i18n = inject(I18nService);
  protected readonly t = this.i18n.t;
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly config = inject(PlatformConfigService);

  private readonly query = inject(ActivatedRoute).snapshot.queryParamMap;
  protected readonly token = this.query.get('token') ?? '';
  /** Whether this invitation asks for the legal texts in force: a staff invitation does, an administrator's never. */
  protected readonly asksConsent = signal(this.query.get('for') === 'staff');
  protected readonly agreed = signal(false);
  private readonly legalVersions = computed(() =>
    this.asksConsent() ? this.config.legal().map((text) => text.versionId) : [],
  );
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
    // Length is not checked here. PasswordPolicy runs against the CONFIGURED minimum and its
    // refusal already names the figure; a literal 8 in the browser would state a rule the platform
    // may not be running and would silently stop matching the day it changes.

    const versions = this.legalVersions();
    if (versions.length && !this.agreed()) {
      this.problem.set(this.t('consent.required'));
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
          {
            token: this.token,
            password,
            // The texts the page showed, in the language it showed them in (Wave 4, W4-8).
            ...(versions.length ? { acceptedLegalVersions: versions, legalLanguage: this.i18n.lang() } : {}),
          },
          { headers: { 'X-XSRF-TOKEN': antiforgery.requestToken } },
        ),
      );
      this.done.set(true);
      setTimeout(() => void this.router.navigateByUrl('/sign-in'), 2500);
    } catch (error) {
      const code: string | undefined = error instanceof HttpErrorResponse ? error.error?.code : undefined;
      if (code === VERSION_NOT_CURRENT || code === CONSENT_REQUIRED) {
        // A text was replaced while this page was open, or this invitation needs one its link did not announce:
        // present the current texts, unticked, rather than failing.
        this.asksConsent.set(true);
        await this.config.reloadLegal();
        this.agreed.set(false);
        this.problem.set(this.t(code === VERSION_NOT_CURRENT ? 'consent.versionChanged' : 'consent.required'));
      } else {
        this.problem.set(describe(error, this.t));
      }
    } finally {
      this.busy.set(false);
    }
  }
}

function describe(error: unknown, t: (key: TranslationKey) => string): string {
  if (!(error instanceof HttpErrorResponse)) {
    return t('common.noResponse');
  }
  const code: string | undefined = error.error?.code;
  if (code === 'auth.invalid_token') {
    return t('acceptInvite.thisInvitationIsNo');
  }
  if (code === 'auth.password_policy' || error.status === 400) {
    return error.error?.title ?? t('acceptInvite.thatPasswordDoesNot');
  }
  if (error.status === 429) {
    return t('common.tooManyAttempts');
  }
  return t('common.noResponse');
}
