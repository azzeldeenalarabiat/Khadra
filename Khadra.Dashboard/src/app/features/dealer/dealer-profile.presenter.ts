import { TranslationKey } from '../../core/i18n/en';
import { Language, MessageParams } from '../../core/i18n/language';
import { DayScheduleInput, UpdateProfileRequest } from '../../core/models/dealer-console.api';
import { DealerProfile } from '../../core/models/dealers.api';
import { LookupEntry } from '../../core/services/lookups.service';

/** Passed in rather than injected: these are pure functions, and their spec calls them directly. */
export type Translate = (key: TranslationKey, params?: MessageParams) => string;

/**
 * The dealer page form as typed: every field a string, because each is bound to an input.
 *
 * The location is part of it. Every save states where the office is — the same city, a new one, or
 * none — because the API replaces the location on each save and will not accept a save that leaves
 * it out. The form used to have no location at all, and every save erased the office's city and
 * address, which took its whole fleet out of every city search.
 */
export interface ProfileForm {
  readonly businessName: string;
  readonly latitude: string;
  readonly longitude: string;
  readonly hours: readonly DayScheduleInput[];
  /** `''` while no city is chosen — what a `<select>` reads — and sent as null. */
  readonly cityId: string;
  readonly area: string;
  readonly street: string;
}

/** One option of the city select. */
export interface CityChoice {
  /** What is sent: the city's id, or `''` for none. */
  readonly id: string;
  readonly label: string;
  /**
   * Rendered as `[selected]` on the option, never `[value]` on the select. The form's city is seeded
   * before the city list arrives, so a value set on the select finds no option to select, the browser
   * shows the first one, and nothing re-applies it when the options appear: the page would show a
   * city the office is not in while the form held the right one.
   */
  readonly selected: boolean;
  /** The office's own city, which the platform no longer offers: kept, never newly chosen. */
  readonly retired: boolean;
}

export interface CityChoices {
  /** Until the list has loaded, the select cannot be used; a save sends the city unchanged. */
  readonly disabled: boolean;
  readonly options: readonly CityChoice[];
}

/**
 * The city select: the platform's offered cities, plus whatever this office is already filed under.
 *
 * Each case below is a way a location could be lost or misstated:
 * - **The list has not loaded, or failed.** That is `null`, never `[]`: an empty list would make
 *   every office's city look retired, and a select with no options cannot hold the one it has. The
 *   select is disabled and the form's city is left alone, so a save sends it unchanged — which the
 *   server keeps without consulting the list.
 * - **The office's city is offered.** It is the selected option, and nothing is added.
 * - **The office's city was retired after it was filed.** Dealers can only fetch the offered list, so
 *   its name is not known here; it is pinned first under a label that says what it is, with its id as
 *   the value, so keeping it keeps it — the server allows a city an office is already filed under. A
 *   DIFFERENT retired city can never be chosen: it is not in the list.
 * - **The office has no city** (rows older than cities). A blank "Choose a city…" leads the list, so
 *   it can file itself. Only then: once an office has a city, the form does not offer to remove it,
 *   because an office with none drops out of every city search. The API still accepts null — this is
 *   the form's choice, not the contract's.
 */
export function cityChoices(
  cities: readonly LookupEntry[] | null,
  filedUnder: string | null,
  chosen: string,
  language: Language,
  t: Translate,
): CityChoices {
  if (cities === null) {
    return {
      disabled: true,
      options: [
        { id: chosen, label: t('dealerProfile.cityListNotLoaded'), selected: true, retired: false },
      ],
    };
  }

  const offered = cities.filter((city) => city.isActive);
  const options: CityChoice[] = [];
  if (filedUnder === null) {
    options.push({
      id: '',
      label: t('dealerApply.chooseACity'),
      selected: chosen === '',
      retired: false,
    });
  } else if (!offered.some((city) => city.id === filedUnder)) {
    options.push({
      id: filedUnder,
      label: t('dealerProfile.currentCityNotOffered'),
      selected: chosen === filedUnder,
      retired: true,
    });
  }
  for (const city of offered) {
    options.push({
      id: city.id,
      label: cityName(city, language),
      selected: chosen === city.id,
      retired: false,
    });
  }
  return { disabled: false, options };
}

/**
 * A city's name in the reader's language, falling back to the other rather than to an empty option:
 * a lookup row may be half translated, and an unnamed option cannot be picked. The same rule as the
 * application form.
 */
function cityName(city: LookupEntry, language: Language): string {
  return (language === 'ar' ? city.nameAr || city.nameEn : city.nameEn || city.nameAr).trim();
}

/** The form as the server last answered it, so the form is a draft of THAT answer. */
export function profileForm(profile: DealerProfile, hours: readonly DayScheduleInput[]): ProfileForm {
  return {
    businessName: profile.businessName,
    latitude: String(profile.latitude),
    longitude: String(profile.longitude),
    hours,
    cityId: profile.cityId ?? '',
    area: profile.address?.area ?? '',
    street: profile.address?.street ?? '',
  };
}

/**
 * The body of a save: every field, every time, the location included.
 *
 * `null` for an empty city or an empty address field, never `undefined` and never a missing key:
 * `JSON.stringify` drops an `undefined` property, and the API refuses a save that leaves the location
 * out — which is exactly what stops a client that forgot it from erasing it.
 */
export function profileRequest(form: ProfileForm): UpdateProfileRequest {
  return {
    businessName: form.businessName.trim(),
    latitude: Number(form.latitude),
    longitude: Number(form.longitude),
    operatingHours: form.hours,
    cityId: form.cityId === '' ? null : form.cityId,
    addressArea: written(form.area),
    addressStreet: written(form.street),
  };
}

/**
 * A street with no area. The server refuses it (`dealer.invalid_address_area`), because a street is
 * somewhere only within an area; the form says so before the owner presses Save. An area alone is
 * fine — many streets have no recorded name.
 */
export function addressIncomplete(form: Pick<ProfileForm, 'area' | 'street'>): boolean {
  return form.street.trim() !== '' && form.area.trim() === '';
}

/** Whether the location in the form differs from the one the server holds. */
export function locationChanged(profile: DealerProfile, form: ProfileForm): boolean {
  return (
    form.cityId !== (profile.cityId ?? '') ||
    (written(form.area) ?? '') !== (profile.address?.area ?? '') ||
    (written(form.street) ?? '') !== (profile.address?.street ?? '')
  );
}

function written(value: string): string | null {
  const trimmed = value.trim();
  return trimmed === '' ? null : trimmed;
}
