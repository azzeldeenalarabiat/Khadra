import { ChangeDetectionStrategy, Component, computed, effect, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LegalSlug, PublicLegalDocument } from '../../core/api/legal.api';
import { httpData } from '../../core/http/http-data';
import { snapshotProblem } from '../../core/http/problem';
import { injectResponseStatus } from '../../core/http/server-context';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { SeoService } from '../../core/seo/seo.service';
import { StatePanelComponent } from '../../shared/state/state-panel.component';

/**
 * The Terms of Service or the Privacy notice, in the version in force (Wave 2 G1; pre-launch item 224).
 *
 * The text is what an administrator published through the console, rendered by the API from a checked Markdown
 * subset. This page adds only the direction and the language of the reader's choice, on the element that holds
 * the text, and Angular sanitises it again as it goes in.
 *
 * With nothing published the page says so plainly and answers 404: no crawler should index an empty legal page.
 * The renderer does not cache the 404, so the first publish shows at once.
 */
@Component({
  selector: 'kh-legal-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, StatePanelComponent],
  templateUrl: './legal-page.component.html',
})
export class LegalPageComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly format = inject(FormatService);

  /** Which text, from the route's data. */
  readonly legal = input<LegalSlug>('terms');

  protected readonly document = httpData<PublicLegalDocument>(() => `/api/v1/legal-documents/${this.legal()}/current`);
  protected readonly problem = computed(() => (this.document.error() ? snapshotProblem(this.document.error()) : null));
  protected readonly notPublished = computed(() => this.problem()?.status === 404);
  protected readonly title = computed(() => this.i18n.t(this.legal() === 'privacy' ? 'legal.privacy' : 'legal.terms'));

  /** The text in the page's language, which the page's own direction already suits. */
  protected readonly html = computed(() => {
    const document = this.document.value();
    if (!document) return '';
    return this.i18n.language() === 'ar' ? document.html.ar : document.html.en;
  });

  constructor() {
    const setStatus = injectResponseStatus();
    const seo = inject(SeoService);

    effect(() => {
      if (this.notPublished()) {
        setStatus(404);
        seo.set({ title: this.i18n.t('seo.legal.title', { title: this.title() }), noindex: true });
        return;
      }
      if (this.problem()) setStatus(503);
      if (!this.document.value()) return;
      seo.set({
        title: this.i18n.t('seo.legal.title', { title: this.title() }),
        description: this.i18n.t(this.legal() === 'privacy' ? 'seo.legal.privacyDescription' : 'seo.legal.termsDescription'),
        path: this.legal(),
      });
    });
  }
}
