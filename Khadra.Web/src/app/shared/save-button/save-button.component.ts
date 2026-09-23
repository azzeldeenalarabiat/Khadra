import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { Router } from '@angular/router';
import { ShortlistService } from '../../core/api/shortlist.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { SessionService } from '../../core/session/session.service';
import { IconComponent } from '../icon/icon.component';

/**
 * The heart. Saved cars are an account list, so a visitor who is not signed in is taken to sign in and
 * brought back to the car; nothing is kept in the browser in the meantime.
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

  protected toggle(event: Event): void {
    event.preventDefault();
    event.stopPropagation();
    if (!this.session.isSignedIn()) {
      void this.router.navigate(this.i18n.link('login'), {
        queryParams: { returnUrl: this.returnUrl() ?? this.router.url },
      });
      return;
    }
    void this.shortlist.toggle(this.vehicleId());
  }
}
