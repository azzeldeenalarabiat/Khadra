import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { I18nService } from '../../core/i18n/i18n.service';
import { SeoService } from '../../core/seo/seo.service';
import { SessionService } from '../../core/session/session.service';
import { IconComponent } from '../../shared/icon/icon.component';

/** The account area's frame: one short side menu, then the page. Never indexed. */
@Component({
  selector: 'kh-account-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, IconComponent],
  templateUrl: './account-shell.component.html',
})
export class AccountShellComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly session = inject(SessionService);

  constructor() {
    inject(SeoService).set({ title: this.i18n.t('seo.account.title'), noindex: true });
  }
}
