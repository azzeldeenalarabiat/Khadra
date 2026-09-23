import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef, Injector, provideZonelessChangeDetection, runInInjectionContext } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { httpData } from './http-data';

describe('httpData', () => {
  let http: HttpTestingController;
  let injector: Injector;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    injector = TestBed.inject(Injector);
  });

  const settle = () => TestBed.inject(ApplicationRef).tick();

  it('answers undefined instead of throwing once the request has failed', async () => {
    const data = runInInjectionContext(injector, () => httpData<{ name: string }>(() => '/api/v1/thing'));
    settle();
    http.expectOne('/api/v1/thing').flush({ code: 'x' }, { status: 503, statusText: 'Unavailable' });
    await TestBed.inject(ApplicationRef).whenStable();

    expect(data.error()).toBeTruthy();
    expect(() => data.value()).not.toThrow();
    expect(data.value()).toBeUndefined();
    expect(data.hasValue()).toBe(false);
  });

  it('carries the value of a request that succeeded, from a url or a request object', async () => {
    const plain = runInInjectionContext(injector, () => httpData<{ name: string }>(() => '/api/v1/a'));
    const shaped = runInInjectionContext(injector, () =>
      httpData<{ name: string }>(() => ({ url: '/api/v1/b', params: { page: 1 } })),
    );
    settle();
    http.expectOne('/api/v1/a').flush({ name: 'a' });
    http.expectOne('/api/v1/b?page=1').flush({ name: 'b' });
    await TestBed.inject(ApplicationRef).whenStable();

    expect(plain.value()).toEqual({ name: 'a' });
    expect(shaped.value()).toEqual({ name: 'b' });
    expect(plain.error()).toBeUndefined();
  });

  it('asks nothing while the request is undefined', () => {
    const data = runInInjectionContext(injector, () => httpData<unknown>(() => undefined));
    settle();
    http.expectNone(() => true);
    expect(data.value()).toBeUndefined();
  });
});
