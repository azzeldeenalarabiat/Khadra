import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LegalSlug } from '../core/api/legal.api';
import { AppConfigService } from '../core/config/app-config.service';
import { I18nService } from '../core/i18n/i18n.service';

/** Deliberately short: where to go, and nothing a marketing page would pad it with. */
@Component({
  selector: 'kh-site-footer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  templateUrl: './site-footer.component.html',
})
export class SiteFooterComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly year = new Date().getFullYear();
  private readonly appConfig = inject(AppConfigService);

  /**
   * The legal texts in force (Wave 2 G1), from `/app-config`: a link only to a page with something on it. None
   * while nothing is published, or while the API cannot say.
   */
  protected readonly legal = computed(() => {
    const documents = this.appConfig.config()?.legal?.documents ?? [];
    const slugs: readonly LegalSlug[] = ['terms', 'privacy'];
    return slugs
      .filter((slug) => documents.some((document) => document.slug === slug))
      .map((slug) => ({ slug, label: this.i18n.t(slug === 'privacy' ? 'legal.privacy' : 'legal.terms') }));
  });
}
