import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface SessionUser {
  readonly id: string;
  readonly email: string;
  readonly fullName: string;
  readonly role: string;
  readonly isEmailVerified: boolean;
  readonly mustChangePassword: boolean;
}

/**
 * Why a sign-in attempt failed, as a case the UI can speak to.
 *
 * The distinction matters: telling someone whose account has been suspended that their password is
 * wrong sends them to reset a password that was never the problem. The API already separates these
 * with a stable `code`, so the console reads that rather than guessing from the status alone.
 */
export type SignInFailure =
  | { readonly kind: 'invalid-credentials' }
  | { readonly kind: 'suspended' }
  | { readonly kind: 'email-not-verified' }
  | { readonly kind: 'rate-limited'; readonly retryAfterSeconds: number | null }
  | { readonly kind: 'unavailable' };

export type SignInResult =
  | { readonly ok: true; readonly user: SessionUser }
  | { readonly ok: false; readonly failure: SignInFailure };

interface AntiforgeryToken {
  readonly requestToken: string;
}

/**
 * The browser's half of the BFF session.
 *
 * Nothing here handles a token: `/bff/login` exchanges credentials for a cookie whose ticket lives in
 * Redis, and the BFF attaches the API bearer token server-side. The console never sees one, which is
 * the whole point of the pattern.
 */
@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly http = inject(HttpClient);

  /** `undefined` means "not asked yet", which is different from "asked, and nobody is signed in". */
  private readonly currentUser = signal<SessionUser | null | undefined>(undefined);

  readonly user = this.currentUser.asReadonly();

  /** Resolves the session once per app load; the guard awaits this before any admin route renders. */
  async load(): Promise<SessionUser | null> {
    const known = this.currentUser();
    if (known !== undefined) return known;

    try {
      const user = await firstValueFrom(this.http.get<SessionUser>('/bff/user'));
      this.currentUser.set(user);
      return user;
    } catch {
      // 401 is the ordinary answer for a visitor who is not signed in, not an error worth surfacing.
      this.currentUser.set(null);
      return null;
    }
  }

  async signIn(email: string, password: string): Promise<SignInResult> {
    try {
      const token = await this.requestToken();
      const user = await firstValueFrom(
        this.http.post<SessionUser>(
          '/bff/login',
          { email, password },
          { headers: { 'X-XSRF-TOKEN': token } },
        ),
      );
      this.currentUser.set(user);
      return { ok: true, user };
    } catch (error) {
      return { ok: false, failure: classify(error) };
    }
  }

  /** The server has already ended the session; only the browser's memory of it is left to clear. */
  /** Spec 4.2 / 6: the BFF re-signs the cookie with the fresh tokens, so other sessions die and this one lives. */
  async changePassword(currentPassword: string, newPassword: string): Promise<void> {
    const token = await this.requestToken();
    const user = await firstValueFrom(
      this.http.post<SessionUser>(
        '/bff/change-password',
        { currentPassword, newPassword },
        { headers: { 'X-XSRF-TOKEN': token } },
      ),
    );
    this.currentUser.set(user);
  }

  forget(): void {
    this.currentUser.set(null);
  }

  async signOut(): Promise<void> {
    try {
      const token = await this.requestToken();
      await firstValueFrom(
        this.http.post('/bff/logout', {}, { headers: { 'X-XSRF-TOKEN': token } }),
      );
    } finally {
      // Whatever the server said, this browser is done with the session.
      this.currentUser.set(null);
    }
  }

  /**
   * The antiforgery token is read from the response body rather than from the cookie, so it works
   * whether or not the BFF marks that cookie readable by script.
   */
  private async requestToken(): Promise<string> {
    const response = await firstValueFrom(this.http.get<AntiforgeryToken>('/bff/antiforgery'));
    return response.requestToken;
  }
}

/** Seconds, from either form the header is allowed to take. */
export function parseRetryAfter(header: string | null): number | null {
  if (!header) return null;

  const seconds = Number(header);
  if (Number.isFinite(seconds) && seconds >= 0) return Math.round(seconds);

  const date = Date.parse(header);
  if (Number.isNaN(date)) return null;
  return Math.max(0, Math.round((date - Date.now()) / 1000));
}

function classify(error: unknown): SignInFailure {
  if (!(error instanceof HttpErrorResponse)) return { kind: 'unavailable' };

  const code: string | undefined = error.error?.code;

  if (error.status === 401) return { kind: 'invalid-credentials' };

  if (error.status === 403) {
    // Both are 403; only the code separates them, and they need different words.
    if (code === 'auth.account_suspended') return { kind: 'suspended' };
    if (code === 'auth.email_not_verified') return { kind: 'email-not-verified' };
    return { kind: 'unavailable' };
  }

  if (error.status === 429) {
    return {
      kind: 'rate-limited',
      retryAfterSeconds: parseRetryAfter(error.headers.get('Retry-After')),
    };
  }

  return { kind: 'unavailable' };
}
