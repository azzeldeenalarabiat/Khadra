import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it } from 'vitest';

import {
  SearchFormComponent,
  SearchFormValue,
} from '../../shared/search-form/search-form.component';
import { I18nService } from '../i18n/i18n.service';
import { LookupsService } from './lookups.service';

const AMMAN = '01a0cb9c-6bef-7dc5-bfe6-3cc9fc12702c';
const CITIES = [{ id: AMMAN, nameEn: 'Amman', nameAr: 'عمّان', isActive: true, displayOrder: 1 }];

const settle = async () => {
  for (let i = 0; i < 3; i++) {
    TestBed.tick();
    await new Promise((resolve) => setTimeout(resolve, 0));
  }
  TestBed.tick();
};

@Component({
  imports: [SearchFormComponent],
  template: '<kh-search-form [value]="value" (submitted)="sent = $event" />',
})
class HostComponent {
  value: SearchFormValue = { city: AMMAN, from: null, to: null, text: null };
  sent: SearchFormValue | null = null;
}

/** Pre-launch item 155: a city filter whose names could not be loaded is not "Any city". */
describe('the city filter while the city list has failed', () => {
  afterEach(() => TestBed.resetTestingModule());

  function setUp() {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    TestBed.inject(I18nService).use('en');
    return { lookups: TestBed.inject(LookupsService), http: TestBed.inject(HttpTestingController) };
  }

  async function answerCities(http: HttpTestingController, fail: boolean) {
    await settle();
    const request = http.expectOne('/api/v1/cities');
    if (fail) request.flush(null, { status: 503, statusText: 'Service Unavailable' });
    else request.flush(CITIES);
    await settle();
  }

  it('names the chosen city, and reads "Any city" only when there is no city', async () => {
    const { lookups, http } = setUp();
    lookups.activeCities();
    await answerCities(http, false);

    expect(lookups.citiesUnavailable()).toBe(false);
    expect(lookups.cityFilterLabel(AMMAN)).toBe('Amman');
    expect(lookups.cityFilterLabel(null)).toBe('Any city');
  });

  it('says the chosen city cannot be named, instead of heading a filtered search "Any city"', async () => {
    const { lookups, http } = setUp();
    lookups.activeCities();
    await answerCities(http, true);

    expect(lookups.citiesUnavailable()).toBe(true);
    expect(lookups.cityFilterLabel(AMMAN)).toBe('Chosen city (city names could not be loaded)');
    // No filter is still no filter: "Any city" is true whether or not the names arrived.
    expect(lookups.cityFilterLabel(null)).toBe('Any city');
  });

  it('shows the search card select as unavailable, and still sends the city it was given', async () => {
    const { http } = setUp();
    const fixture: ComponentFixture<HostComponent> = TestBed.createComponent(HostComponent);
    await answerCities(http, true);
    fixture.detectChanges();
    await settle();

    const select = (fixture.nativeElement as HTMLElement).querySelector<HTMLSelectElement>(
      'select[name=city]',
    )!;
    expect(select.disabled).toBe(true);
    expect([...select.options].map((option) => option.textContent?.trim())).toEqual([
      'City list unavailable',
    ]);

    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLFormElement>('form')!
      .dispatchEvent(new Event('submit'));
    expect(fixture.componentInstance.sent?.city).toBe(AMMAN);
  });

  it('offers every city, and "Any city", once the list has arrived', async () => {
    const { http } = setUp();
    const fixture: ComponentFixture<HostComponent> = TestBed.createComponent(HostComponent);
    await answerCities(http, false);
    fixture.detectChanges();
    await settle();

    const select = (fixture.nativeElement as HTMLElement).querySelector<HTMLSelectElement>(
      'select[name=city]',
    )!;
    expect(select.disabled).toBe(false);
    expect([...select.options].map((option) => option.textContent?.trim())).toEqual([
      'Any city',
      'Amman',
    ]);
  });
});
