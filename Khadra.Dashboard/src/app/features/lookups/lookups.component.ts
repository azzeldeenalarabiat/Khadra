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

  protected readonly title = computed(() => (this.isCities() ? 'Cities & regions' : 'Car types'));

  protected readonly subtitle = computed(() =>
    this.isCities()
      ? 'The places customers search in. A retired city stays on every dealership and booking that already names it.'
      : 'The categories a car is listed under. A retired type stays on every vehicle that already names it.',
  );

  protected readonly activeCount = computed(
    () => this.entries().filter((entry) => entry.isActive).length,
  );

  protected readonly failure = computed(() => {
    const error = this.resource.error() as { status?: number } | undefined;
    if (!error) return null;
    if (error.status === 403) return 'Platform lookups are curated by administrators.';
    return 'The list could not be loaded. Nothing has been changed.';
  });

  protected add(): void {
    const cities = this.isCities();
    this.ui.openAction(
      {
        icon: 'plus-circle',
        tone: 'accent',
        title: cities ? 'Add a city' : 'Add a car type',
        body: cities
          ? 'Customers filter their search by this list, in whichever language they are using.'
          : 'Dealers choose from this list when they list a car, and customers filter by it.',
        fields: [
          {
            name: 'nameEn',
            label: 'Name (English)',
            type: 'text',
            placeholder: cities ? 'e.g. Amman' : 'e.g. Sedan',
          },
          {
            name: 'nameAr',
            label: 'Name (Arabic)',
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
        confirm: cities ? 'Add city' : 'Add car type',
        result: { title: 'Added', body: '', tone: 'ok' },
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
      { title: 'Added', body: '' },
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
          { name: 'nameEn', label: 'Name (English)', type: 'text', placeholder: '', value: entry.nameEn },
          { name: 'nameAr', label: 'Name (Arabic)', type: 'text', placeholder: '', value: entry.nameAr },
        ],
        confirm: 'Rename',
        result: { title: 'Renamed', body: '', tone: 'ok' },
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
      { title: 'Renamed', body: '' },
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
          ? 'It is offered again on new listings and searches.'
          : 'It stops being offered on new listings and searches. Everything already using it is untouched — this is not a delete, and there is no delete.',
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
