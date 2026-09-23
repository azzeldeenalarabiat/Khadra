import { ChangeDetectionStrategy, Component, afterNextRender, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { I18nService } from '../core/i18n/i18n.service';
import { SessionService } from '../core/session/session.service';
import { SiteFooterComponent } from './site-footer.component';
import { SiteHeaderComponent } from './site-header.component';

/**
 * The customer website's frame: header, page, footer. Its own layout — nothing here is shared with
 * the staff console's shell, which assumes a signed-in operator on a desk.
 */
@Component({
  selector: 'kh-site-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, SiteHeaderComponent, SiteFooterComponent],
  templateUrl: './site-shell.component.html',
})
export class SiteShellComponent {
  protected readonly i18n = inject(I18nService);

  constructor() {
    // Only the browser can know who is signed in; the server renders every page signed-out-neutral.
    const session = inject(SessionService);
    afterNextRender(() => void session.resolve());
  }
}
