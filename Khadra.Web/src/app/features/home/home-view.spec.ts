import { describe, expect, it } from 'vitest';
import { CatalogueFacets, CatalogueListing } from '../../core/api/catalogue.api';
import { LookupEntry } from '../../core/api/common.api';
import { heroShowcase, homeCategories } from './home-view';

const listing = (id: string, cover: string | null) => ({ vehicleId: id, coverImageUrl: cover }) as unknown as CatalogueListing;
const type = (id: string, nameEn: string, nameAr: string, displayOrder: number): LookupEntry =>
  ({ id, nameEn, nameAr, isActive: true, displayOrder, createdAt: '' }) as unknown as LookupEntry;
const facets = (ids: string[], carTypes?: CatalogueFacets['carTypes']): CatalogueFacets =>
  ({ seats: [], carTypeIds: ids, makes: [], fuelTypes: [], years: [], ...(carTypes ? { carTypes } : {}) });

describe('the home hero', () => {
  it('shows only real photos, newest first, never a placeholder', () => {
    const shots = heroShowcase([listing('a', null), listing('b', '/b.jpg'), listing('c', '/c.jpg'), listing('d', '/d.jpg'), listing('e', '/e.jpg')], 3);
    expect(shots.map((car) => car.vehicleId)).toEqual(['b', 'c', 'd']);
  });

  it('shows nothing when no listing has a photo', () => {
    expect(heroShowcase([listing('a', null)], 3)).toEqual([]);
  });
});

describe('the vehicle-type cards', () => {
  const sedan = type('t1', 'Sedan', 'سيدان', 1);
  const suv = type('t2', 'SUV', 'دفع رباعي', 2);
  const van = type('t3', 'Van', 'فان', 3);

  it('offers only the types a bookable car has, in the administrator order, named in the reader language', () => {
    const cards = homeCategories([sedan, suv, van], facets(['t3', 't1']), true);
    expect(cards.map((card) => card.name)).toEqual(['سيدان', 'فان']);
  });

  it('takes the count and photo from the facets, and says nothing when the API did not report them', () => {
    const withSummary = homeCategories([sedan, suv], facets(['t1', 't2'], [{ carTypeId: 't1', listedVehicleCount: 4, coverImageUrl: '/s.jpg' }]), false);
    expect(withSummary).toEqual([
      { id: 't1', name: 'Sedan', vehicleCount: 4, coverImageUrl: '/s.jpg' },
      { id: 't2', name: 'SUV', vehicleCount: null, coverImageUrl: null },
    ]);
  });

  it('never offers a type whose count is zero', () => {
    const cards = homeCategories([sedan], facets(['t1'], [{ carTypeId: 't1', listedVehicleCount: 0, coverImageUrl: null }]), false);
    expect(cards).toEqual([]);
  });

  it('draws nothing before the facets answer', () => {
    expect(homeCategories([sedan], undefined, false)).toEqual([]);
  });
});
