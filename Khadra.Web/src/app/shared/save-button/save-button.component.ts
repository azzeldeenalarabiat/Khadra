import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ShortlistService } from '../../core/api/shortlist.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { SessionService } from '../../core/session/session.service';
import { withSaveIntent } from '../../core/session/return-address';
import { IconComponent } from '../icon/icon.component';

/**
 * The heart. Saved cars are an account list, so a visitor who is not signed in is taken to sign in and brought back to
 * the car, with the car to save carried in the return address; once signed in, it is saved (Wave 3 E5; E2E F12). The
 * list itself is never kept in the browser.
 */
@Component({
  selector: 'kh-save-button',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [IconComponent],
  templateUrl: './save-button.component.html',
})
export class SaveButtonComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly session = inject(SessionService);
  private readonly shortlist = inject(ShortlistService);
  private readonly router = inject(Router);

  readonly vehicleId = input.required<string>();
  readonly returnUrl = input<string | null>(null);

  protected readonly saved = computed(() => this.shortlist.savedIds().has(this.vehicleId()));
  protected readonly busy = computed(() => this.shortlist.busyIds().has(this.vehicleId()));
  /** Set by a tap, never by a page load: only a heart the visitor just filled answers with a pop. */
  protected readonly justSaved = signal(false);

  protected toggle(event: Event): void {
    event.preventDefault();
    event.stopPropagation();
    if (!this.session.isSignedIn()) {
      void this.router.navigate(this.i18n.link('login'), {
        queryParams: { returnUrl: withSaveIntent(this.returnUrl() ?? this.router.url, this.vehicleId()) },
      });
      return;
    }
    this.justSaved.set(!this.saved());
    void this.shortlist.toggle(this.vehicleId());
  }
}
