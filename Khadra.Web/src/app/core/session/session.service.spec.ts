import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { describe, expect, it } from 'vitest';
import { classifySignInFailure } from './session.service';

const refusal = (status: number, code?: string, headers?: Record<string, string>) =>
  new HttpErrorResponse({ status, error: code ? { code } : null, headers: new HttpHeaders(headers ?? {}) });

describe('classifySignInFailure', () => {
  it('reads a wrong password as invalid credentials', () => {
    expect(classifySignInFailure(refusal(401, 'auth.invalid_credentials'))).toEqual({ kind: 'invalid-credentials' });
  });

  // A staff account on the customer website: the API accepted the password and the BFF refused the role.
  // The visitor must be told it is the wrong kind of account, not that the password is wrong.
  it('reads a role this site does not serve as the wrong kind of account', () => {
    expect(classifySignInFailure(refusal(403, 'bff.role_not_allowed'))).toEqual({ kind: 'wrong-account-type' });
  });

  it('tells a suspended account and an unverified email apart', () => {
    expect(classifySignInFailure(refusal(403, 'auth.account_suspended'))).toEqual({ kind: 'suspended' });
    expect(classifySignInFailure(refusal(403, 'auth.email_not_verified'))).toEqual({ kind: 'email-not-verified' });
  });

  it('carries the wait the limiter gave', () => {
    expect(classifySignInFailure(refusal(429, 'rate_limit.exceeded', { 'Retry-After': '540' }))).toEqual({
      kind: 'rate-limited',
      retryAfterSeconds: 540,
    });
  });

  it('says only "wait" when no wait was given', () => {
    expect(classifySignInFailure(refusal(429))).toEqual({ kind: 'rate-limited', retryAfterSeconds: null });
  });

  it('reads anything else as unavailable', () => {
    expect(classifySignInFailure(refusal(502))).toEqual({ kind: 'unavailable' });
    expect(classifySignInFailure(new Error('network'))).toEqual({ kind: 'unavailable' });
  });
});
