import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PublicGalleryCard } from '../../core/api/catalogue.api';
import { LookupsService } from '../../core/api/lookups.service';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { slugFor } from '../../core/routing/slug';
import { IconComponent } from '../icon/icon.component';

/** A rental office in a list: logo, name, city, how many cars, whether it delivers. */
@Component({
  selector: 'kh-office-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, IconComponent],
  templateUrl: './office-card.component.html',
})
export class OfficeCardComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly format = inject(FormatService);
  private readonly lookups = inject(LookupsService);

  readonly office = input.required<PublicGalleryCard>();

  protected readonly link = computed(() =>
    this.i18n.link('dealers', slugFor(this.office().dealerId, this.office().businessName)),
  );
  protected readonly city = computed(() => this.lookups.cityName(this.office().cityId));
}
