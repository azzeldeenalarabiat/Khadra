import { describe, expect, it } from 'vitest';
import { AR } from '../../core/i18n/ar';
import { EN } from '../../core/i18n/en';
import { resolveMessage } from '../../core/i18n/resolve';
import { DealerProfile } from '../../core/models/dealers.api';
import { LookupEntry } from '../../core/services/lookups.service';
import {
  ProfileForm,
  Translate,
  addressIncomplete,
  cityChoices,
  locationChanged,
  profileForm,
  profileRequest,
} from './dealer-profile.presenter';

/**
 * Where a dealership is, on the dealer page form.
 *
 * Every save of this form used to erase the office's city and address, which took its whole fleet
 * out of every city search. The form now shows the location and sends it on every save; these are
 * the rules that keep that from losing or misstating it.
 *
 * The real dictionaries are resolved, so the assertions read as the words an owner sees.
 */
const en: Translate = (key, params) => resolveMessage(EN[key], params, 'en-GB', false) ?? key;
const ar: Translate = (key, params) =>
  resolveMessage(AR[key], params, 'ar-JO-u-nu-latn', true) ?? key;

const AMMAN = '01a08222-0228-7f9f-b051-fb28aca2ac4c';
const IRBID = '01a08222-65c0-7bba-933e-74274446bb9e';
const ZARQA = '01a08222-7d10-7c7a-a4be-2f2c3a91e0d1';

const city = (id: string, nameEn: string, nameAr: string, displayOrder: number): LookupEntry => ({
  id,
  nameEn,
  nameAr,
  isActive: true,
  displayOrder,
  createdAt: '2026-09-01T09:00:00Z',
  centreLatitude: null,
  centreLongitude: null,
});

/** What `GET /cities` returns to a dealer: the offered cities only. Irbid is half translated. */
const OFFERED: readonly LookupEntry[] = [city(AMMAN, 'Amman', 'عمّان', 1), city(IRBID, 'Irbid', '', 2)];

function profile(over: Partial<DealerProfile> = {}): DealerProfile {
  return {
    dealerId: '01a08222-0000-7000-8000-000000000001',
    businessName: 'Petra Rentals',
    commercialRegistrationNumber: '123456',
    verificationStatus: 'Approved',
    reviewNote: null,
    suspensionReason: null,
    submittedAt: '2026-09-01T09:00:00Z',
    reviewDueAt: '2026-09-03T09:00:00Z',
    createdAt: '2026-09-01T09:00:00Z',
    canTrade: true,
    isSuspended: false,
    submittedDocuments: [],
    missingDocuments: [],
    requiredDocuments: [],
    description: { ar: null, en: null },
    latitude: 31.95,
    longitude: 35.91,
    cityId: AMMAN,
    address: { area: 'Abdoun', street: 'Zahran Street' },
    operatingHours: [],
    delivery: { isEnabled: false, radiusKm: 0, fee: null },
    logoUrl: null,
    coverUrl: null,
    employeeCount: 0,
    isOwner: true,
    canViewReports: true,
    ...over,
  };
}

const form = (over: Partial<ProfileForm> = {}): ProfileForm => ({
  ...profileForm(profile(), []),
  ...over,
});

describe('the dealer page location', () => {
  describe('the city select', () => {
    it('keeps the city as it is, and cannot be used, until the list has loaded', () => {
      // `null`, not `[]`: an empty list would call every office's city retired.
      const choices = cityChoices(null, AMMAN, AMMAN, 'en', en);

      expect(choices.disabled).toBe(true);
      expect(choices.options).toEqual([
        { id: AMMAN, label: EN['dealerProfile.cityListNotLoaded'], selected: true, retired: false },
      ]);
    });

    it('selects the office’s city when the platform offers it, and adds nothing', () => {
      const choices = cityChoices(OFFERED, AMMAN, AMMAN, 'en', en);

      expect(choices.disabled).toBe(false);
      expect(choices.options.map((option) => option.id)).toEqual([AMMAN, IRBID]);
      expect(choices.options.filter((option) => option.selected).map((option) => option.id)).toEqual([
        AMMAN,
      ]);
      expect(choices.options.some((option) => option.retired)).toBe(false);
    });

    it('keeps a city retired since the office was filed under it, pinned first and selected', () => {
      const choices = cityChoices(OFFERED, ZARQA, ZARQA, 'en', en);

      expect(choices.options[0]).toEqual({
        id: ZARQA,
        label: EN['dealerProfile.currentCityNotOffered'],
        selected: true,
        retired: true,
      });
      // Nothing is offered that the platform does not list: another retired city cannot be chosen.
      expect(choices.options.slice(1).map((option) => option.id)).toEqual([AMMAN, IRBID]);
    });

    it('still offers the retired city back after the owner picks another', () => {
      const choices = cityChoices(OFFERED, ZARQA, IRBID, 'en', en);

      expect(choices.options.find((option) => option.retired)?.selected).toBe(false);
      expect(choices.options.find((option) => option.id === IRBID)?.selected).toBe(true);
    });

    it('lets an office with no city choose one, and never pins anything for it', () => {
      const choices = cityChoices(OFFERED, null, '', 'en', en);

      expect(choices.options[0]).toEqual({
        id: '',
        label: EN['dealerApply.chooseACity'],
        selected: true,
        retired: false,
      });
      expect(choices.options.some((option) => option.retired)).toBe(false);
    });

    it('does not offer "no city" once the office has one', () => {
      // An office with no city drops out of every city search. The API accepts null; the form does
      // not put it one click away.
      for (const filed of [AMMAN, ZARQA])
        expect(cityChoices(OFFERED, filed, filed, 'en', en).options.some((option) => option.id === '')).toBe(
          false,
        );
    });

    it('names cities in the reader’s language, and falls back rather than leave one blank', () => {
      const labels = cityChoices(OFFERED, AMMAN, AMMAN, 'ar', ar).options.map((option) => option.label);

      expect(labels).toEqual(['عمّان', 'Irbid']);
    });
  });

  describe('the save', () => {
    it('sends every field, the location included, on every save', () => {
      expect(Object.keys(profileRequest(form())).sort()).toEqual([
        'addressArea',
        'addressStreet',
        'businessName',
        'cityId',
        'latitude',
        'longitude',
        'operatingHours',
      ]);
    });

    it('sends the location the server holds when the owner changed something else', () => {
      const body = profileRequest(form({ businessName: 'Petra Rentals ' }));

      expect(body.cityId).toBe(AMMAN);
      expect(body.addressArea).toBe('Abdoun');
      expect(body.addressStreet).toBe('Zahran Street');
    });

    it('sends null — never undefined, never a missing key — for an empty city or address', () => {
      // `JSON.stringify` drops undefined; the API refuses a save that leaves the location out.
      const body = profileRequest(form({ cityId: '', area: '  ', street: '' }));

      expect(body.cityId).toBeNull();
      expect(body.addressArea).toBeNull();
      expect(body.addressStreet).toBeNull();
      expect(JSON.parse(JSON.stringify(body))).toHaveProperty('cityId', null);
      expect(JSON.parse(JSON.stringify(body))).toHaveProperty('addressArea', null);
      expect(JSON.parse(JSON.stringify(body))).toHaveProperty('addressStreet', null);
    });

    it('trims what was typed', () => {
      const body = profileRequest(form({ area: '  Sweifieh ', street: ' Wakalat Street  ' }));

      expect(body.addressArea).toBe('Sweifieh');
      expect(body.addressStreet).toBe('Wakalat Street');
    });

    it('holds a street with no area back before the server has to refuse it', () => {
      expect(addressIncomplete({ area: '', street: 'Zahran Street' })).toBe(true);
      expect(addressIncomplete({ area: '   ', street: 'Zahran Street' })).toBe(true);
      expect(addressIncomplete({ area: 'Abdoun', street: '' })).toBe(false);
      expect(addressIncomplete({ area: '', street: '' })).toBe(false);
    });
  });

  describe('changes', () => {
    it('reads a freshly loaded form as unchanged', () => {
      expect(locationChanged(profile(), form())).toBe(false);
      expect(locationChanged(profile({ cityId: null, address: null }), profileForm(profile({ cityId: null, address: null }), []))).toBe(
        false,
      );
    });

    it('sees a new city, area or street, and not whitespace around the same ones', () => {
      expect(locationChanged(profile(), form({ cityId: IRBID }))).toBe(true);
      expect(locationChanged(profile(), form({ area: 'Sweifieh' }))).toBe(true);
      expect(locationChanged(profile(), form({ street: '' }))).toBe(true);
      expect(locationChanged(profile(), form({ area: ' Abdoun ' }))).toBe(false);
    });
  });
});
