import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { vocabularyLabel } from '../../core/api/app-config.api';
import { CatalogueListing } from '../../core/api/catalogue.api';
import { LookupsService } from '../../core/api/lookups.service';
import { AppConfigService } from '../../core/config/app-config.service';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { slugFor } from '../../core/routing/slug';
import { IconComponent } from '../icon/icon.component';
import { SaveButtonComponent } from '../save-button/save-button.component';

/** One car in a grid: photo, name, the facts that decide a rental, the daily rate, and its office. */
@Component({
  selector: 'kh-car-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, IconComponent, SaveButtonComponent],
  templateUrl: './car-card.component.html',
})
export class CarCardComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly format = inject(FormatService);
  private readonly lookups = inject(LookupsService);
  private readonly appConfig = inject(AppConfigService);

  readonly car = input.required<CatalogueListing>();
  /** Query parameters carried to the car's page, so chosen dates follow the customer there. */
  readonly carry = input<Record<string, string>>({});
  /** The first row of a page: load its photo eagerly, it is what the visitor sees first. */
  readonly eager = input(false);

  protected readonly link = computed(() => {
    const car = this.car();
    return this.i18n.link('cars', slugFor(car.vehicleId, car.make, car.model, car.year));
  });

  protected readonly city = computed(() => this.lookups.cityName(this.car().gallery.cityId));

  protected readonly transmission = computed(() =>
    vocabularyLabel(this.appConfig.config()?.vocabularies.transmissions, this.car().transmission, this.i18n.isArabic()),
  );

  protected readonly fuel = computed(() =>
    vocabularyLabel(this.appConfig.config()?.vocabularies.fuelTypes, this.car().fuelType, this.i18n.isArabic()),
  );
}
