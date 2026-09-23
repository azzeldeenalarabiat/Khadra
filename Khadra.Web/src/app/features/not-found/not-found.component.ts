import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { injectResponseStatus } from '../../core/http/server-context';
import { I18nService } from '../../core/i18n/i18n.service';
import { SeoService } from '../../core/seo/seo.service';
import { IconComponent } from '../../shared/icon/icon.component';

/** A real 404: the status is set on the server, so a crawler drops the address instead of indexing this. */
@Component({
  selector: 'kh-not-found',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, IconComponent],
  templateUrl: './not-found.component.html',
})
export class NotFoundComponent {
  protected readonly i18n = inject(I18nService);

  constructor() {
    injectResponseStatus()(404);
    inject(SeoService).set({ title: this.i18n.t('seo.notFound.title'), noindex: true });
  }
}
