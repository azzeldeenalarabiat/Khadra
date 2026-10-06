import { ChangeDetectionStrategy, Component, afterNextRender, computed, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ShortlistService } from '../core/api/shortlist.service';
import { I18nService } from '../core/i18n/i18n.service';
import { SessionService } from '../core/session/session.service';
import { SiteFooterComponent } from './site-footer.component';
import { SiteHeaderComponent } from './site-header.component';
import { IconComponent } from '../shared/icon/icon.component';

/**
 * The customer website's frame: header, page, footer. Its own layout — nothing here is shared with
 * the staff console's shell, which assumes a signed-in operator on a desk.
 */
@Component({
  selector: 'kh-site-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, SiteHeaderComponent, SiteFooterComponent, IconComponent],
  templateUrl: './site-shell.component.html',
})
export class SiteShellComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly shortlist = inject(ShortlistService);
  /**
   * Why a heart did not hold, said where the visitor can see it whatever they had scrolled to (E2E F70): the list is
   * full, or the car could not be saved. Nothing was saved in either case.
   */
  protected readonly shortlistRefusal = computed(() => {
    const refusal = this.shortlist.lastRefusal();
    if (!refusal) return null;
    return this.i18n.t(refusal.code === 'shortlist.full' ? 'saved.full' : 'saved.couldNotSave');
  });

  constructor() {
    // Only the browser can know who is signed in; the server renders every page signed-out-neutral.
    const session = inject(SessionService);
    afterNextRender(() => void session.resolve());
  }
}
