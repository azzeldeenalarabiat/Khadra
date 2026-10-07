import { ChangeDetectionStrategy, Component, afterNextRender, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { ShortlistService } from '../core/api/shortlist.service';
import { I18nService } from '../core/i18n/i18n.service';
import { ConsentGateService } from '../core/session/consent-gate.service';
import { SessionService } from '../core/session/session.service';
import { ConsentPromptComponent } from './consent-prompt.component';
import { SiteFooterComponent } from './site-footer.component';
import { SiteHeaderComponent } from './site-header.component';
import { IconComponent } from '../shared/icon/icon.component';

/** The legal texts' own pages, in either language. */
const LEGAL_PAGE = /^\/(?:en|ar)\/(?:terms|privacy)(?:[/?#]|$)/;

/**
 * The customer website's frame: header, page, footer. Its own layout — nothing here is shared with
 * the staff console's shell, which assumes a signed-in operator on a desk.
 */
@Component({
  selector: 'kh-site-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, SiteHeaderComponent, SiteFooterComponent, IconComponent, ConsentPromptComponent],
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

  private readonly consent = inject(ConsentGateService);
  private readonly router = inject(Router);
  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => this.router.url),
    ),
    { initialValue: this.router.url },
  );

  /**
   * The prompt in place of the page while a text awaits this customer's consent (Wave 4, W4-8), on every page but the
   * texts' own: the prompt opens those in a new tab, and that tab must show the text rather than the prompt again.
   */
  protected readonly consentBlocked = computed(
    () => this.consent.blocked() && !LEGAL_PAGE.test(this.url()),
  );

  constructor() {
    // Only the browser can know who is signed in; the server renders every page signed-out-neutral.
    const session = inject(SessionService);
    afterNextRender(() => void session.resolve());
  }
}
