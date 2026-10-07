import { ChangeDetectionStrategy, Component, computed, inject, input, model } from '@angular/core';
import { I18nService } from '../../core/i18n/i18n.service';
import { consentSentence } from '../../core/i18n/legal-kind';
import { LEGAL_KINDS } from '../../core/models/legal.api';
import { PlatformConfigService } from '../../core/services/platform-config.service';

/**
 * The checkbox somebody ticks to accept the legal texts in force as they join (Wave 4, W4-8): registering a rental
 * office, or accepting a staff invitation. Shown only while a text is in force — with nothing published there is
 * nothing to accept, and the server asks for nothing.
 *
 * Every link is the public page the API named on `/app-config`, in the reader's language; a text whose page the API
 * could not name is still named, unlinked, never pointed somewhere invented.
 */
@Component({
  selector: 'kh-legal-consent',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './legal-consent.component.html',
})
export class LegalConsentComponent {
  private readonly i18n = inject(I18nService);
  private readonly config = inject(PlatformConfigService);

  readonly agreed = model(false);
  readonly disabled = input(false);

  /** The sentence, in pieces: words, and each text's name as a link to its page. */
  protected readonly parts = computed(() => {
    const arabic = this.i18n.lang() === 'ar';
    const documents = this.config.legal();
    const texts = LEGAL_KINDS.flatMap((kind) => {
      const document = documents.find((candidate) => candidate.kind === kind);
      if (!document) return [];
      const url = document.pageUrls ? (arabic ? document.pageUrls.ar : document.pageUrls.en) : null;
      return [{ kind, url }];
    });
    return consentSentence(texts, this.i18n.t);
  });

  protected checked(event: Event): boolean {
    return (event.target as HTMLInputElement).checked;
  }
}
