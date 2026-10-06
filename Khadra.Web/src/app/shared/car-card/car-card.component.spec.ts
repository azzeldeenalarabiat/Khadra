import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, describe, expect, it } from 'vitest';

import { CatalogueListing } from '../../core/api/catalogue.api';
import { I18nService } from '../../core/i18n/i18n.service';
import { CarCardComponent } from './car-card.component';

const car = (extra: Partial<CatalogueListing> = {}): CatalogueListing =>
  ({
    vehicleId: '01a0cb9c-6bef-7dc5-bfe6-3cc9fc12702c',
    make: 'Hyundai',
    model: 'Elantra',
    year: 2024,
    carType: null,
    transmission: 'Automatic',
    fuelType: 'Petrol',
    seats: 5,
    coverImageUrl: null,
    dailyRate: { amount: 40, currency: 'JOD' },
    isDeliveryAvailable: true,
    gallery: { dealerId: 'd-1', businessName: 'Arabiat Car', cityId: null, logoUrl: null, averageRating: null, reviewCount: 0 },
    ...extra,
  }) as CatalogueListing;

describe('CarCardComponent, at the searched times (Wave 3 E7)', () => {
  afterEach(() => TestBed.resetTestingModule());

  function render(listing: CatalogueListing, language: 'ar' | 'en' = 'en'): string {
    TestBed.configureTestingModule({
      imports: [CarCardComponent],
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    TestBed.inject(I18nService).use(language);
    const fixture = TestBed.createComponent(CarCardComponent);
    fixture.componentRef.setInput('car', listing);
    fixture.detectChanges();
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  it('says a car comes by delivery only when the office is shut at those times', () => {
    expect(render(car({ selfPickupAvailable: false }))).toContain('Delivery only at these times');
    TestBed.resetTestingModule();
    expect(render(car({ selfPickupAvailable: false }), 'ar')).toContain('بالتوصيل فقط في هذه الأوقات');
  });

  it('keeps the plain delivery badge when the counter is open, or when no times were asked about', () => {
    expect(render(car({ selfPickupAvailable: true }))).toContain('Delivery available');
    TestBed.resetTestingModule();
    const undated = render(car());
    expect(undated).toContain('Delivery available');
    expect(undated).not.toContain('Delivery only');
  });
});
