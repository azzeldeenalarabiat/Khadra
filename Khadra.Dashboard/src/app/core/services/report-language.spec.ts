import { HttpClient, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { reportLanguage } from './report-language';

describe('reportLanguage (Wave 3, C6)', () => {
  const setup = () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    return { http: TestBed.inject(HttpClient), server: TestBed.inject(HttpTestingController) };
  };

  it('stores the language the console is read in, for the emails the platform sends', async () => {
    const { http, server } = setup();

    const sent = reportLanguage(http, 'ar');
    const request = server.expectOne('/api/v1/auth/me/language');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ language: 'ar' });
    request.flush(null, { status: 204, statusText: 'No Content' });

    await expect(sent).resolves.toBeUndefined();
    server.verify();
  });

  it('never fails the sign-in or the switch when the server refuses', async () => {
    const { http, server } = setup();

    const sent = reportLanguage(http, 'en');
    server
      .expectOne('/api/v1/auth/me/language')
      .flush(null, { status: 503, statusText: 'Unavailable' });

    await expect(sent).resolves.toBeUndefined();
  });
});
