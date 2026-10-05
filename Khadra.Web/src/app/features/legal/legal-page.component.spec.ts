import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef, RESPONSE_INIT, provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { LegalSlug, PublicLegalDocument } from '../../core/api/legal.api';
import { I18nService } from '../../core/i18n/i18n.service';
import { Language } from '../../core/i18n/language';
import { legalPaths } from '../../../sitemap';
import { LegalPageComponent } from './legal-page.component';

/**
 * A legal text's page (Wave 2 G1): the version in force, in the page's language with its direction stated on the
 * text, and an honest 404 while nothing is published.
 */
const TERMS: PublicLegalDocument = {
  kind: 'Terms',
  versionId: '01a10e5a-7c2e-7d1a-9f3e-2b9a1c0d4e5f',
  versionLabel: '2026-10',
  effectiveFrom: '2026-10-05T06:00:00+00:00',
  publishedAt: '2026-10-05T06:00:00+00:00',
  html: { en: '<h1>Terms</h1>\n<p>Welcome to <strong>Khadra</strong>.</p>\n', ar: '<h1>الشروط</h1>\n<p>مرحبًا بك في <strong>خضرا</strong>.</p>\n' },
};

describe('a legal page', () => {
  let http: HttpTestingController;
  let response: { status?: number };

  beforeEach(() => {
    response = {};
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: RESPONSE_INIT, useValue: response },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function open(slug: LegalSlug, language: Language, answer: (url: string) => void): Promise<HTMLElement> {
    TestBed.inject(I18nService).use(language);
    const fixture: ComponentFixture<LegalPageComponent> = TestBed.createComponent(LegalPageComponent);
    fixture.componentRef.setInput('legal', slug);
    TestBed.inject(ApplicationRef).tick();
    answer(`/api/v1/legal-documents/${slug}/current`);
    await TestBed.inject(ApplicationRef).whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('shows the version in force in the page language, stating its direction on the text', async () => {
    const page = await open('terms', 'ar', (url) => http.expectOne(url).flush(TERMS));

    expect(page.querySelector('#legal-title')?.textContent?.trim()).toBe('شروط الخدمة');
    const text = page.querySelector('.legal-text')!;
    expect(text.getAttribute('dir')).toBe('rtl');
    expect(text.getAttribute('lang')).toBe('ar');
    expect(text.querySelector('strong')?.textContent).toBe('خضرا');
    expect(page.textContent).toContain('2026-10');
    expect(response.status).toBeUndefined();
  });

  it('shows the English text left to right on the English page', async () => {
    const page = await open('terms', 'en', (url) => http.expectOne(url).flush(TERMS));

    const text = page.querySelector('.legal-text')!;
    expect(text.getAttribute('dir')).toBe('ltr');
    expect(text.querySelector('h1')?.textContent).toBe('Terms');
  });

  it('says plainly that nothing is published, and answers 404 so no crawler indexes it', async () => {
    const page = await open('privacy', 'en', (url) =>
      http.expectOne(url).flush({ code: 'legal.not_published', title: 'Not published.' }, { status: 404, statusText: 'Not Found' }),
    );

    expect(page.textContent).toContain('This document has not been published yet.');
    expect(page.querySelector('.legal-text')).toBeNull();
    expect(response.status).toBe(404);
  });

  it('answers 503 while the API is not answering, so the page is retried rather than indexed', async () => {
    await open('terms', 'en', (url) => http.expectOne(url).flush({}, { status: 503, statusText: 'Unavailable' }));

    expect(response.status).toBe(503);
  });
});

describe('the pages sitemap', () => {
  it('lists a legal page only while a version of it is in force', () => {
    expect(legalPaths([])).toEqual([]);
    expect(legalPaths([{ slug: 'privacy' }])).toEqual(['privacy']);
    expect(legalPaths([{ slug: 'privacy' }, { slug: 'terms' }, { slug: 'cookies' }])).toEqual(['terms', 'privacy']);
  });
});
