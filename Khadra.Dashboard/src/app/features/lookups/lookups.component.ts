import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { map } from 'rxjs';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { loaded } from '../../core/services/loaded';
import { LookupEntry, LookupKind, LookupsService } from '../../core/services/lookups.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { I18nService } from '../../core/i18n/i18n.service';

/**
 * The two lists an administrator curates: car types and cities (spec 3.2).
 *
 * One component for both, because they are the same aggregate with the same four actions; the route
 * says which. Bilingual from day one — the customer app ships in Arabic and English, and a list with
 * one name would force one of them to read a language it did not choose.
 *
 * Nothing here deletes. Retiring an entry keeps it off new cars and new searches while every vehicle
 * and booking already pointing at it goes on reading correctly; these ids are referenced across
 * bounded contexts by id, and nothing joins back to warn you what a delete would break.
 */
@Component({
  selector: 'kh-lookups',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './lookups.component.html',
  imports: [IconComponent],
})
export class LookupsComponent {
  protected readonly t = inject(I18nService).t;
  private readonly service = inject(LookupsService);
  private readonly ui = inject(ConsoleUiService);
  private readonly route = inject(ActivatedRoute);

  private readonly kindFromRoute = toSignal(
    this.route.data.pipe(map((data) => data['kind'] as LookupKind)),
    { initialValue: this.route.snapshot.data['kind'] as LookupKind },
  );

  constructor() {
    effect(() => this.service.kind.set(this.kindFromRoute()));
  }

  protected readonly resource = this.service.entries;
  protected readonly entries = computed(() => loaded(this.resource)() ?? []);
  protected readonly kind = computed(() => this.kindFromRoute());
  protected readonly isCities = computed(() => this.kind() === 'cities');

  protected readonly title = computed(() => (this.isCities() ? 'Cities & regions' : this.t('lookups.carTypes')));

  protected readonly subtitle = computed(() =>
    this.isCities()
      ? this.t('lookups.thePlacesCustomersSearch')
      : this.t('lookups.theCategoriesACar'),
  );

  protected readonly activeCount = computed(
    () => this.entries().filter((entry) => entry.isActive).length,
  );

  protected readonly failure = computed(() => {
    const error = this.resource.error() as { status?: number } | undefined;
    if (!error) return null;
    if (error.status === 403) return this.t('lookups.platformLookupsAreCurated');
    return this.t('lookups.theListCouldNot');
  });

  protected add(): void {
    const cities = this.isCities();
    this.ui.openAction(
      {
        icon: 'plus-circle',
        tone: 'accent',
        title: cities ? 'Add a city' : this.t('lookups.addACarType'),
        body: cities
          ? this.t('lookups.customersFilterTheirSearch')
          : this.t('lookups.dealersChooseFromThis'),
        fields: [
          {
            name: 'nameEn',
            label: this.t('lookups.nameEnglish'),
            type: 'text',
            placeholder: cities ? 'e.g. Amman' : 'e.g. Sedan',
          },
          {
            name: 'nameAr',
            label: this.t('lookups.nameArabic'),
            type: 'text',
            placeholder: cities ? 'مثال: عمّان' : 'مثال: سيدان',
          },
          { name: 'displayOrder', label: this.t('lookups.displayOrder'), type: 'text', placeholder: '0' },
          ...(cities
            ? [
                {
                  name: 'centreLat',
                  label: this.t('lookups.centreLatitude'),
                  type: 'text' as const,
                  placeholder: 'Optional, e.g. 31.9539',
                  optional: true,
                },
                {
                  name: 'centreLng',
                  label: this.t('lookups.centreLongitude'),
                  type: 'text' as const,
                  placeholder: 'Optional, e.g. 35.9106',
                  optional: true,
                },
              ]
            : []),
        ],
        confirm: cities ? this.t('lookups.addCity') : this.t('lookups.addCarType'),
        result: { title: this.t('adminUsers.added'), body: '', tone: 'ok' },
      },
      async (values) => {
        const body: Record<string, unknown> = {
          nameEn: values['nameEn'] ?? '',
          nameAr: values['nameAr'] ?? '',
          displayOrder: Number(values['displayOrder'] ?? '0') || 0,
        };
        if (cities) {
          // Both or neither: half a coordinate is not a place, and the server refuses one alone.
          const latitude = values['centreLat']?.trim();
          const longitude = values['centreLng']?.trim();
          if (latitude && longitude) {
            body['latitude'] = Number(latitude);
            body['longitude'] = Number(longitude);
          }
        }
        await this.service.create(this.kind(), body);
        this.service.refresh();
      },
      { title: this.t('adminUsers.added'), body: '' },
    );
  }

  protected rename(entry: LookupEntry): void {
    this.ui.openAction(
      {
        icon: 'pencil-simple',
        tone: 'accent',
        title: `Rename ${entry.nameEn}`,
        body: this.t('lookups.bothNamesChangeTogether'),
        fields: [
          { name: 'nameEn', label: this.t('lookups.nameEnglish'), type: 'text', placeholder: '', value: entry.nameEn },
          { name: 'nameAr', label: this.t('lookups.nameArabic'), type: 'text', placeholder: '', value: entry.nameAr },
        ],
        confirm: 'Rename',
        result: { title: this.t('lookups.renamed'), body: '', tone: 'ok' },
      },
      async (values) => {
        await this.service.rename(
          this.kind(),
          entry.id,
          values['nameEn'] ?? '',
          values['nameAr'] ?? '',
        );
        this.service.refresh();
      },
      { title: this.t('lookups.renamed'), body: '' },
    );
  }

  protected setActive(entry: LookupEntry, isActive: boolean): void {
    this.ui.openAction(
      {
        icon: isActive ? 'check-circle' : 'eye-slash',
        tone: isActive ? 'ok' : 'warn',
        danger: !isActive,
        title: isActive ? `Restore ${entry.nameEn}?` : `Retire ${entry.nameEn}?`,
        body: isActive
          ? this.t('lookups.itIsOfferedAgain')
          : this.t('lookups.itStopsBeingOffered'),
        confirm: isActive ? 'Restore' : 'Retire',
        result: {
          title: isActive ? 'Restored' : 'Retired',
          body: '',
          tone: isActive ? 'ok' : 'warn',
        },
      },
      async () => {
        await this.service.setActive(this.kind(), entry.id, isActive);
        this.service.refresh();
      },
      { title: isActive ? 'Restored' : 'Retired', body: '' },
    );
  }

  protected reload(): void {
    this.resource.reload();
  }

  protected centre(entry: LookupEntry): string | null {
    if (entry.centreLatitude === null || entry.centreLongitude === null) return null;
    return `${entry.centreLatitude.toFixed(4)}, ${entry.centreLongitude.toFixed(4)}`;
  }
}
