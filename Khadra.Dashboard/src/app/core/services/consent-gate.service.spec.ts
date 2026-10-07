// Loaded before anything Angular, and only here: `HttpClient` is a DI token below. See platform-config.service.spec.
import '@angular/compiler';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injector, WritableSignal, runInInjectionContext, signal } from '@angular/core';
import { Observable, of, throwError } from 'rxjs';
import { describe, expect, it } from 'vitest';
import { LegalConfigDocument, MyLegalConsents } from '../models/legal.api';
import { ConsentGateService, isConsentPending } from './consent-gate.service';
import { SessionService, SessionUser } from './session.service';

/**
 * The console's half of the consent gate (Wave 4, W4-8; owner D6 with administrators exempt): what it asks, for whom,
 * and what an acceptance does. The server is the gate; these pin that the console says so once, about the right
 * person, and lets go the moment the texts are accepted.
 */
const TERMS: LegalConfigDocument = {
  kind: 'Terms',
  slug: 'terms',
  versionId: 'v-terms-2',
  versionLabel: '2026-10',
  effectiveFrom: '2026-10-07T00:00:00Z',
  pageUrls: { en: 'https://khadra.test/en/terms', ar: 'https://khadra.test/ar/terms' },
};

const PRIVACY: LegalConfigDocument = { ...TERMS, kind: 'Privacy', slug: 'privacy', versionId: 'v-privacy-1' };

function user(id: string, role: string): SessionUser {
  return { id, email: `${id}@khadra.test`, fullName: id, role, isEmailVerified: true, mustChangePassword: false };
}

interface Posted {
  url: string;
  body: unknown;
  headers: Record<string, string> | undefined;
}

/** The two endpoints the gate calls, answered from a queue the test fills. */
class FakeHttp {
  readonly asked: string[] = [];
  readonly posted: Posted[] = [];
  answers: MyLegalConsents[] = [];
  acceptance: () => Observable<MyLegalConsents> = () => of({ accepted: [], pending: [] });

  get(url: string): Observable<unknown> {
    this.asked.push(url);
    if (url === '/bff/antiforgery') return of({ requestToken: 'xsrf-1' });
    const next = this.answers.shift();
    return next ? of(next) : throwError(() => new HttpErrorResponse({ status: 503 }));
  }

  post(url: string, body: unknown, options?: { headers?: Record<string, string> }): Observable<unknown> {
    this.posted.push({ url, body, headers: options?.headers });
    return this.acceptance();
  }
}

function gateFor(signedIn: SessionUser | null): {
  gate: ConsentGateService;
  http: FakeHttp;
  session: WritableSignal<SessionUser | null>;
} {
  const http = new FakeHttp();
  const session = signal<SessionUser | null>(signedIn);
  const injector = Injector.create({
    providers: [
      { provide: HttpClient, useValue: http },
      { provide: SessionService, useValue: { user: session.asReadonly() } },
    ],
  });
  return { gate: runInInjectionContext(injector, () => new ConsentGateService()), http, session };
}

const consentsAsked = (http: FakeHttp): number =>
  http.asked.filter((url) => url === '/api/v1/auth/me/legal-consents').length;

describe('the consent gate', () => {
  it('asks once per person whether a text awaits them, and blocks while one does', async () => {
    const { gate, http } = gateFor(user('owner-1', 'DealerOwner'));
    http.answers.push({ accepted: [], pending: [TERMS, PRIVACY] });

    await gate.settle();
    await gate.settle();

    expect(consentsAsked(http)).toBe(1);
    expect(gate.blocked()).toBe(true);
    expect(gate.pending().map((text) => text.versionId)).toEqual(['v-terms-2', 'v-privacy-1']);
  });

  it('never asks about an administrator, whom the texts do not address, and never blocks one', async () => {
    const { gate, http } = gateFor(user('admin-1', 'Admin'));

    await gate.settle();
    gate.raise();

    expect(consentsAsked(http)).toBe(0);
    expect(gate.blocked()).toBe(false);
  });

  it('holds what it learned about one person, never about whoever signs in next', async () => {
    const { gate, http, session } = gateFor(user('owner-1', 'DealerOwner'));
    http.answers.push({ accepted: [], pending: [TERMS] });
    await gate.settle();
    expect(gate.blocked()).toBe(true);

    session.set(user('employee-2', 'DealerEmployee'));
    expect(gate.blocked()).toBe(false);

    session.set(null);
    expect(gate.blocked()).toBe(false);
  });

  it('asks again when a refusal says a text was published while the console was open', async () => {
    const { gate, http } = gateFor(user('owner-1', 'DealerOwner'));
    http.answers.push({ accepted: [], pending: [] }, { accepted: [], pending: [TERMS] });
    await gate.settle();
    expect(gate.blocked()).toBe(false);

    gate.raise();
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(consentsAsked(http)).toBe(2);
    expect(gate.blocked()).toBe(true);
  });

  it('accepts exactly the texts it showed, in the language they were read in, and lets go', async () => {
    const { gate, http } = gateFor(user('owner-1', 'DealerOwner'));
    http.answers.push({ accepted: [], pending: [TERMS, PRIVACY] });
    await gate.settle();

    const answer = await gate.accept('ar');

    expect(answer).toBe('accepted');
    expect(http.posted).toEqual([
      {
        url: '/api/v1/auth/me/legal-consents',
        body: { versionIds: ['v-terms-2', 'v-privacy-1'], language: 'ar' },
        headers: { 'X-XSRF-TOKEN': 'xsrf-1' },
      },
    ]);
    expect(gate.blocked()).toBe(false);
  });

  it('re-presents the current texts when one was replaced between showing and accepting', async () => {
    const { gate, http } = gateFor(user('owner-1', 'DealerOwner'));
    const replaced: LegalConfigDocument = { ...TERMS, versionId: 'v-terms-3', versionLabel: '2026-11' };
    http.answers.push({ accepted: [], pending: [TERMS] }, { accepted: [], pending: [replaced] });
    http.acceptance = () =>
      throwError(
        () => new HttpErrorResponse({ status: 409, error: { code: 'legal.version_not_current' } }),
      );
    await gate.settle();

    const answer = await gate.accept('en');

    expect(answer).toBe('changed');
    expect(gate.blocked()).toBe(true);
    expect(gate.pending().map((text) => text.versionId)).toEqual(['v-terms-3']);
  });

  it('stays closed when the acceptance was not recorded', async () => {
    const { gate, http } = gateFor(user('owner-1', 'DealerOwner'));
    http.answers.push({ accepted: [], pending: [TERMS] });
    http.acceptance = () => throwError(() => new HttpErrorResponse({ status: 503 }));
    await gate.settle();

    expect(await gate.accept('en')).toBe('failed');
    expect(gate.blocked()).toBe(true);
  });

  it('does not stop anybody it could not ask: the server still refuses, and its refusal asks again', async () => {
    const { gate } = gateFor(user('owner-1', 'DealerOwner'));

    await gate.settle();

    expect(gate.blocked()).toBe(false);
    expect(gate.open()).toBe(true);
  });

  // Found in the local run: services left standing by an earlier session in the same tab fired the moment the next
  // person signed in, before the answer, and were refused. They read `open`, which waits for the answer.
  it('lets nothing go on a person’s behalf until their consents are known, and nothing while one is pending', async () => {
    const { gate, http, session } = gateFor(user('owner-1', 'DealerOwner'));
    http.answers.push({ accepted: [], pending: [TERMS] });

    expect(gate.open()).toBe(false);
    await gate.settle();
    expect(gate.open()).toBe(false);

    await gate.accept('en');
    expect(gate.open()).toBe(true);

    session.set(user('employee-2', 'DealerEmployee'));
    expect(gate.open()).toBe(false);
  });

  it('is open for an administrator at once: the texts do not address Khadra’s staff', () => {
    const { gate } = gateFor(user('admin-1', 'Admin'));

    expect(gate.open()).toBe(true);
  });

  it('knows its own refusal from every other 403', () => {
    expect(isConsentPending(new HttpErrorResponse({ status: 403, error: { code: 'legal.consent_pending' } }))).toBe(true);
    expect(isConsentPending(new HttpErrorResponse({ status: 403, error: { code: 'dealers.not_trading' } }))).toBe(false);
    expect(isConsentPending(new HttpErrorResponse({ status: 403 }))).toBe(false);
    expect(isConsentPending(new HttpErrorResponse({ status: 409, error: { code: 'legal.consent_pending' } }))).toBe(false);
    expect(isConsentPending(new Error('legal.consent_pending'))).toBe(false);
  });
});
