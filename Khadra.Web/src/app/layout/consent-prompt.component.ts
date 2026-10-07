import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { LegalSlug } from '../core/api/legal.api';
import { TranslationKey } from '../core/i18n/en';
import { I18nService } from '../core/i18n/i18n.service';
import { ConsentGateService } from '../core/session/consent-gate.service';
import { SessionService } from '../core/session/session.service';
import { legalName } from '../features/legal/consent-sentence';
import { IconComponent } from '../shared/icon/icon.component';

/**
 * The page, while a legal text in force waits for the signed-in customer's consent (Wave 4, W4-8; owner D6: blocked,
 * not a banner). The server answers nothing else until it is accepted, so this offers exactly what resolves it: the
 * texts themselves — each opens its own page in a new tab, which the shell lets through — the acceptance, and signing
 * out. The acceptance is recorded in the language of the page, because that is the text the links open.
 */
@Component({
  selector: 'kh-consent-prompt',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, IconComponent],
  templateUrl: './consent-prompt.component.html',
})
export class ConsentPromptComponent {
  protected readonly i18n = inject(I18nService);
  private readonly consent = inject(ConsentGateService);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);

  protected readonly agreed = signal(false);
  protected readonly busy = signal(false);
  /** A key, never a sentence: a language switch re-words a refusal already on screen. */
  protected readonly problem = signal<TranslationKey | null>(null);

  /** Each text in force still to accept, named in the reader's language, with its page here when the website has one. */
  protected readonly texts = computed(() =>
    this.consent.pending().map((text) => {
      const slug: LegalSlug | null = text.slug === 'terms' || text.slug === 'privacy' ? text.slug : null;
      return {
        versionId: text.versionId,
        slug,
        label: slug ? legalName(slug, this.i18n.t.bind(this.i18n)) : text.kind,
        version: text.versionLabel,
      };
    }),
  );

  protected checked(event: Event): boolean {
    return (event.target as HTMLInputElement).checked;
  }

  protected async accept(): Promise<void> {
    if (this.busy() || !this.agreed()) return;
    this.busy.set(true);
    this.problem.set(null);
    try {
      const answer = await this.consent.accept(this.i18n.language());
      if (answer === 'changed') {
        // A newer text came into force while this one was open: it is what is listed now, and it has not been read.
        this.agreed.set(false);
        this.problem.set('consent.changed');
      } else if (answer === 'failed') {
        this.problem.set('consent.failed');
      }
    } finally {
      this.busy.set(false);
    }
  }

  protected async signOut(): Promise<void> {
    await this.session.signOut();
    void this.router.navigate(this.i18n.link());
  }
}
