import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { map } from 'rxjs';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { loaded } from '../../core/services/loaded';
import { LookupEntry, LookupKind, LookupsService } from '../../core/services/lookups.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { TranslationKey } from '../../core/i18n/en';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { Language } from '../../core/i18n/language';
import { ProblemSnapshot, serverSentence, snapshotProblem } from '../../core/i18n/problem';

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
  private readonly i18n = inject(I18nService);
  protected readonly t = this.i18n.t;
  private readonly formats = inject(FormatService);
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

  protected readonly title = computed(() =>
    this.isCities() ? this.t('lookups.citiesRegions') : this.t('lookups.carTypes'),
  );

  protected readonly subtitle = computed(() =>
    this.isCities()
      ? this.t('lookups.thePlacesCustomersSearch')
      : this.t('lookups.theCategoriesACar'),
  );

  protected readonly activeCount = computed(
    () => this.entries().filter((entry) => entry.isActive).length,
  );

  protected readonly failure = computed(() => {
    const error = this.resource.error();
    if (!error) return null;
    return describe(snapshotProblem(error), this.t, this.i18n.lang());
  });

  /**
   * The entry's name in the language the console is speaking.
   *
   * The table shows both columns side by side on purpose — this is the screen that curates them, and
   * an administrator adding a city needs to see what a customer reading either language will see. A
   * dialog title is a sentence, so the name inside it follows the reader instead. The name itself is
   * never translated here: both spellings come from the server.
   */
  private entryName(entry: LookupEntry): string {
    return this.i18n.lang() === 'ar' && entry.nameAr.trim() !== '' ? entry.nameAr : entry.nameEn;
  }

  protected add(): void {
    const cities = this.isCities();
    this.ui.openAction(
      {
        icon: 'plus-circle',
        tone: 'accent',
        title: cities ? this.t('lookups.addACity') : this.t('lookups.addACarType'),
        body: cities
          ? this.t('lookups.customersFilterTheirSearch')
          : this.t('lookups.dealersChooseFromThis'),
        fields: [
          {
            name: 'nameEn',
            label: this.t('lookups.nameEnglish'),
            type: 'text',
            // The example stays in the field's own script — this box holds the English name however
            // the console is worded — and only "e.g." follows the reader.
            placeholder: cities
              ? this.t('lookups.exampleCityEnglish')
              : this.t('lookups.exampleCarTypeEnglish'),
          },
          {
            name: 'nameAr',
            label: this.t('lookups.nameArabic'),
            type: 'text',
            placeholder: cities
              ? this.t('lookups.exampleCityArabic')
              : this.t('lookups.exampleCarTypeArabic'),
          },
          { name: 'displayOrder', label: this.t('lookups.displayOrder'), type: 'text', placeholder: '0' },
          ...(cities
            ? [
                {
                  name: 'centreLat',
                  label: this.t('lookups.centreLatitude'),
                  type: 'text' as const,
                  placeholder: this.t('lookups.optionalExampleLatitude'),
                  optional: true,
                },
                {
                  name: 'centreLng',
                  label: this.t('lookups.centreLongitude'),
                  type: 'text' as const,
                  placeholder: this.t('lookups.optionalExampleLongitude'),
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
        title: this.t('lookups.renameEntry', { name: this.entryName(entry) }),
        body: this.t('lookups.bothNamesChangeTogether'),
        fields: [
          { name: 'nameEn', label: this.t('lookups.nameEnglish'), type: 'text', placeholder: '', value: entry.nameEn },
          { name: 'nameAr', label: this.t('lookups.nameArabic'), type: 'text', placeholder: '', value: entry.nameAr },
        ],
        confirm: this.t('lookups.rename'),
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
    const name = this.entryName(entry);
    const done = isActive ? this.t('lookups.restored') : this.t('lookups.retired');
    this.ui.openAction(
      {
        icon: isActive ? 'check-circle' : 'eye-slash',
        tone: isActive ? 'ok' : 'warn',
        danger: !isActive,
        title: isActive
          ? this.t('lookups.restoreEntry', { name })
          : this.t('lookups.retireEntry', { name }),
        body: isActive ? this.t('lookups.itIsOfferedAgain') : this.t('lookups.itStopsBeingOffered'),
        confirm: isActive ? this.t('lookups.restore') : this.t('lookups.retire'),
        result: {
          title: done,
          body: '',
          tone: isActive ? 'ok' : 'warn',
        },
      },
      async () => {
        await this.service.setActive(this.kind(), entry.id, isActive);
        this.service.refresh();
      },
      { title: done, body: '' },
    );
  }

  protected reload(): void {
    this.resource.reload();
  }

  protected centre(entry: LookupEntry): string | null {
    if (entry.centreLatitude === null || entry.centreLongitude === null) return null;
    // One isolated run: two numbers joined by a comma are laid out longitude-first under Arabic.
    return this.formats.coordinates(entry.centreLatitude, entry.centreLongitude);
  }
}

function describe(
  problem: ProblemSnapshot,
  t: (key: TranslationKey) => string,
  language: Language,
): string {
  if (problem.status === 403) return t('lookups.platformLookupsAreCurated');
  return serverSentence(problem, language, t) ?? t('lookups.theListCouldNot');
}
