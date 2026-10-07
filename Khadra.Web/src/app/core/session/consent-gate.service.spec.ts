import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { LegalConfigDocument } from '../api/legal.api';
import { ConsentGateService, isConsentPending } from './consent-gate.service';
import { SessionService, SessionUser } from './session.service';

/**
 * The website's half of the consent gate (Wave 4, W4-8; owner D6: blocked, not a banner). The server is the gate; these
 * pin that the website asks as soon as somebody is signed in, about that person only, and lets go once they accept.
 */
const CONSENTS = '/api/v1/auth/me/legal-consents';
const settle = () => new Promise((resolve) => setTimeout(resolve));

const TERMS: LegalConfigDocument = {
  kind: 'Terms',
  slug: 'terms',
  versionId: 'v-terms-2',
  versionLabel: '2026-10',
  effectiveFrom: '2026-10-07T00:00:00Z',
  pageUrls: { en: 'https://khadra.test/en/terms', ar: 'https://khadra.test/ar/terms' },
};

const customer = (id: string): SessionUser => ({
  id,
  email: `${id}@khadra.test`,
  fullName: id,
  phone: '+962790000000',
  role: 'Customer',
  isEmailVerified: true,
  mustChangePassword: false,
});

describe('the consent gate', () => {
  const user = signal<SessionUser | null>(null);
  let http: HttpTestingController;
  let gate: ConsentGateService;

  beforeEach(() => {
    user.set(null);
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SessionService, useValue: { user } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    gate = TestBed.inject(ConsentGateService);
  });

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  async function signIn(id: string, pending: readonly LegalConfigDocument[]): Promise<void> {
    user.set(customer(id));
    TestBed.tick();
    http.expectOne(CONSENTS).flush({ accepted: [], pending });
    await settle();
  }

  it('asks nothing of a visitor who is not signed in', () => {
    TestBed.tick();
    http.expectNone(CONSENTS);
    expect(gate.blocked()).toBe(false);
    expect(gate.clear()).toBe(false);
  });

  it('asks as soon as somebody signs in, and blocks while a text awaits them', async () => {
    await signIn('rana', [TERMS]);

    expect(gate.blocked()).toBe(true);
    expect(gate.clear()).toBe(false);
    expect(gate.pending().map((text) => text.versionId)).toEqual(['v-terms-2']);
  });

  it('is clear for somebody who owes nothing', async () => {
    await signIn('rana', []);

    expect(gate.blocked()).toBe(false);
    expect(gate.clear()).toBe(true);
  });

  it('holds what it learned about one person, never about whoever signs in next', async () => {
    await signIn('rana', [TERMS]);

    user.set(null);
    expect(gate.blocked()).toBe(false);

    await signIn('omar', []);
    expect(gate.blocked()).toBe(false);
  });

  it('accepts exactly the texts it showed, in the language they were read in, and lets go', async () => {
    await signIn('rana', [TERMS]);

    const accepting = gate.accept('ar');
    const post = http.expectOne(CONSENTS);
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ versionIds: ['v-terms-2'], language: 'ar' });
    post.flush({ accepted: [], pending: [] });

    expect(await accepting).toBe('accepted');
    expect(gate.blocked()).toBe(false);
    expect(gate.clear()).toBe(true);
  });

  it('re-presents the current texts when one was replaced between showing and accepting', async () => {
    await signIn('rana', [TERMS]);
    const replaced = { ...TERMS, versionId: 'v-terms-3' };

    const accepting = gate.accept('en');
    http
      .expectOne((request) => request.method === 'POST' && request.url === CONSENTS)
      .flush({ code: 'legal.version_not_current' }, { status: 409, statusText: 'Conflict' });
    await settle();
    http.expectOne((request) => request.method === 'GET' && request.url === CONSENTS).flush({ accepted: [], pending: [replaced] });

    expect(await accepting).toBe('changed');
    expect(gate.pending().map((text) => text.versionId)).toEqual(['v-terms-3']);
  });

  it('asks again when a refusal says a text came into force while the page was open', async () => {
    await signIn('rana', []);

    gate.raise();
    http.expectOne(CONSENTS).flush({ accepted: [], pending: [TERMS] });
    await settle();

    expect(gate.blocked()).toBe(true);
  });

  it('does not hold back what somebody asked for when it cannot ask: the server still refuses', async () => {
    user.set(customer('rana'));
    TestBed.tick();
    http.expectOne(CONSENTS).flush(null, { status: 503, statusText: 'Unavailable' });
    await settle();

    expect(gate.blocked()).toBe(false);
    expect(gate.clear()).toBe(true);
  });

  it('knows its own refusal from every other 403', () => {
    expect(isConsentPending(new HttpErrorResponse({ status: 403, error: { code: 'legal.consent_pending' } }))).toBe(true);
    expect(isConsentPending(new HttpErrorResponse({ status: 403, error: { code: 'bff.role_not_allowed' } }))).toBe(false);
    expect(isConsentPending(new HttpErrorResponse({ status: 409, error: { code: 'legal.consent_pending' } }))).toBe(false);
  });
});
