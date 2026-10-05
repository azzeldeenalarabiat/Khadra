import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { I18nService } from '../../core/i18n/i18n.service';
import { legalKindLabel } from '../../core/i18n/legal-kind';
import { LEGAL_KINDS } from '../../core/models/legal.api';
import { PlatformConfigService } from '../../core/services/platform-config.service';

/**
 * Links to the legal texts in force, on the pages where somebody joins or signs in (Wave 2 G1).
 *
 * Every link is a page the API named on `/app-config`, in the reader's language: nobody types a URL here. A text
 * with nothing in force has no link. While the API names no page at all (no website address set, or it could not
 * say), the row is absent rather than pointing somewhere invented. Consent itself, the checkbox, comes in Wave 4.
 */
@Component({
  selector: 'kh-legal-links',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './legal-links.component.html',
})
export class LegalLinksComponent {
  private readonly i18n = inject(I18nService);
  protected readonly t = this.i18n.t;
  private readonly config = inject(PlatformConfigService);

  protected readonly links = computed(() => {
    const documents = this.config.legal();
    const arabic = this.i18n.lang() === 'ar';
    return LEGAL_KINDS.flatMap((kind) => {
      const urls = documents.find((document) => document.kind === kind)?.pageUrls;
      return urls ? [{ kind, label: legalKindLabel(kind, this.t), url: arabic ? urls.ar : urls.en }] : [];
    });
  });
}
