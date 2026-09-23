import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
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
}
