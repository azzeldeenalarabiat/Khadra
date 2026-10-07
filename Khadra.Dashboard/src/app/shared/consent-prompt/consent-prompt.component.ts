import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { TranslationKey } from '../../core/i18n/en';
import { I18nService } from '../../core/i18n/i18n.service';
import { legalKindLabel } from '../../core/i18n/legal-kind';
import { ConsentGateService } from '../../core/services/consent-gate.service';
import { SessionService } from '../../core/services/session.service';
import { IconComponent } from '../icon/icon.component';

/**
 * The page, while a legal text in force waits for this person's consent (Wave 4, W4-8; owner D6: blocked, not a
 * banner). The server answers nothing else until it is accepted, so this offers exactly what resolves it: the texts
 * themselves, on the public pages the API named, the acceptance, and signing out.
 *
 * The acceptance is recorded in the language the console is showing, because that is the text the links open.
 */
@Component({
  selector: 'kh-consent-prompt',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './consent-prompt.component.html',
  imports: [IconComponent],
})
export class ConsentPromptComponent {
  private readonly i18n = inject(I18nService);
  protected readonly t = this.i18n.t;
  private readonly consent = inject(ConsentGateService);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);

  protected readonly agreed = signal(false);
  protected readonly busy = signal(false);
  /** A key, never a sentence: a language switch re-words a refusal already on screen. */
  protected readonly problem = signal<TranslationKey | null>(null);

  /** Each text in force still to accept, named in the reader's language, linked only to a page the API named. */
  protected readonly texts = computed(() => {
    const arabic = this.i18n.lang() === 'ar';
    return this.consent.pending().map((text) => ({
      versionId: text.versionId,
      label: legalKindLabel(text.kind, this.t),
      version: text.versionLabel,
      url: text.pageUrls ? (arabic ? text.pageUrls.ar : text.pageUrls.en) : null,
    }));
  });

  protected checked(event: Event): boolean {
    return (event.target as HTMLInputElement).checked;
  }

  protected async accept(): Promise<void> {
    if (this.busy() || !this.agreed()) return;
    this.busy.set(true);
    this.problem.set(null);
    try {
      const answer = await this.consent.accept(this.i18n.lang());
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
    await this.router.navigateByUrl('/sign-in');
  }
}
