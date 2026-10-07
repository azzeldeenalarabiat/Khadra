import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { TranslationKey } from '../../core/i18n/en';
import { I18nService } from '../../core/i18n/i18n.service';
import { CONSENT_REQUIRED, VERSION_NOT_CURRENT } from '../../core/services/consent-gate.service';
import { PlatformConfigService } from '../../core/services/platform-config.service';
import { LanguageSwitchComponent } from '../../shared/language-switch/language-switch.component';
import { IconComponent } from '../../shared/icon/icon.component';
import { LegalConsentComponent } from '../../shared/legal-consent/legal-consent.component';
import { LegalLinksComponent } from '../../shared/legal-links/legal-links.component';

/**
 * Step one of spec 3.1: the person behind a rental office gets an account.
 *
 * Two steps, not one, and the screen says so. This creates the USER — role DealerOwner from the
 * start — and nothing else; the business itself, its commercial registration and its licence papers
 * are submitted from inside the console once they can sign in. Merging the two would mean holding
 * three uploaded documents against an address nobody has yet proved belongs to them.
 *
 * No date of birth is asked for. Spec 5.1's minimum age governs who may RENT a car, and the person
 * who owns the rental office is not renting one; their identity is proved by the ID document an
 * administrator reads at licence review. The server no longer asks for it either.
 *
 * While a legal text is in force, the owner accepts it here, and the account and the acceptance are saved together
 * (Wave 4, W4-8): the server refuses a registration without it.
 */
@Component({
  selector: 'kh-register-dealer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './register-dealer.component.html',
  imports: [FormsModule, RouterLink, IconComponent, LanguageSwitchComponent, LegalConsentComponent, LegalLinksComponent],
})
export class RegisterDealerComponent {
  private readonly i18n = inject(I18nService);
  protected readonly t = this.i18n.t;
  private readonly http = inject(HttpClient);
  private readonly config = inject(PlatformConfigService);

  protected readonly fullName = signal('');
  protected readonly email = signal('');
  protected readonly phone = signal('');
  protected readonly password = signal('');
  /** The legal texts in force, accepted. Asked only while one is in force. */
  protected readonly agreed = signal(false);
  private readonly legalVersions = computed(() => this.config.legal().map((text) => text.versionId));

  protected readonly busy = signal(false);
  protected readonly problem = signal<string | null>(null);
  /** The address the account was created for, so the confirmation names where to look. */
  protected readonly registered = signal<string | null>(null);
  /**
   * Whether the verification email actually went out.
   *
   * The server tells us, and the confirmation says whichever is true. This screen used to promise
   * “we sent a verification link” unconditionally, because the dispatcher swallowed delivery
   * failures — so a mail outage sent people to wait at an inbox nothing was coming to.
   */
  protected readonly emailSent = signal(true);

  protected fieldValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected async submit(): Promise<void> {
    if (this.busy()) return;

    const versions = this.legalVersions();
    const body = {
      fullName: this.fullName().trim(),
      email: this.email().trim(),
      phone: this.phone().trim(),
      password: this.password(),
      // The texts the page showed, in the language it showed them in (Wave 4, W4-8).
      ...(versions.length ? { acceptedLegalVersions: versions, legalLanguage: this.i18n.lang() } : {}),
    };

    if (!body.fullName || !body.email || !body.phone || !body.password) {
      this.problem.set(this.t('registerDealer.fillInEveryField'));
      return;
    }

    if (versions.length && !this.agreed()) {
      this.problem.set(this.t('consent.required'));
      return;
    }

    this.busy.set(true);
    this.problem.set(null);
    try {
      const issued = await firstValueFrom(
        this.http.get<{ requestToken: string }>('/bff/antiforgery'),
      );
      const created = await firstValueFrom(
        this.http.post<{ userId: string; email: string; verificationEmailSent: boolean }>(
          '/api/v1/auth/register-dealer-owner',
          body,
          { headers: { 'X-XSRF-TOKEN': issued.requestToken } },
        ),
      );
      this.registered.set(created.email);
      this.emailSent.set(created.verificationEmailSent);
    } catch (error) {
      const code: string | undefined = error instanceof HttpErrorResponse ? error.error?.code : undefined;
      if (code === VERSION_NOT_CURRENT || code === CONSENT_REQUIRED) {
        // A text was replaced while this page was open, or one is in force that this page never showed: present the
        // current texts, unticked, rather than failing.
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

/**
 * The server's `code` decides the message.
 *
 * "This email is already registered" is deliberate here and deliberately absent from sign-in: a
 * registration form has to say the address is taken or the person cannot proceed, whereas sign-in
 * saying it would hand an attacker a list of who holds an account.
 */
type Translate = (key: TranslationKey) => string;

function describe(error: unknown, t: Translate): string {
  if (!(error instanceof HttpErrorResponse)) {
    return t('auth.register.err.noResponse');
  }

  const code: string | undefined = error.error?.code;
  switch (code) {
    case 'auth.email_taken':
      return t('auth.register.err.emailTaken');
    case 'auth.phone_taken':
      return t('auth.register.err.phoneTaken');
    case 'auth.invalid_phone':
      return t('auth.register.err.invalidPhone');
    default:
      break;
  }

  if (error.status === 429) {
    return t('auth.register.err.rateLimited');
  }
  // A validation failure carries its own reason AND the configured figure behind it — the password
  // minimum, the name bounds — so the server's title beats anything invented here.
  // The server's own title carries the configured figure behind the rule -- the password minimum,
  // the name bounds -- so it beats anything invented here. It is still English until the API
  // negotiates a language; see the checklist.
  return error.error?.title ?? t('auth.register.err.rejected');
}
