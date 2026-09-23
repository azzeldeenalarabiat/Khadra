import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, PLATFORM_ID, computed, inject, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { firstValueFrom } from 'rxjs';
import { XsrfService } from '../http/xsrf.service';

/** `GET /bff/user`: who holds this browser's session, as the BFF recorded it at sign-in. */
export interface SessionUser {
  readonly id: string;
  readonly email: string;
  readonly fullName: string;
  readonly phone: string;
  readonly role: string;
  readonly isEmailVerified: boolean;
  readonly mustChangePassword: boolean;
}

/**
 * `unknown` until the browser has asked. The server always renders `unknown`: it never holds the
 * visitor's cookie, so it cannot know, and a page that rendered "Sign in" on the server would flash it
 * at every signed-in customer before hydration corrected it.
 */
export type SessionState =
  | { readonly status: 'unknown' }
  | { readonly status: 'anonymous' }
  | { readonly status: 'signed-in'; readonly user: SessionUser };

export type SignInFailure =
  | { readonly kind: 'invalid-credentials' }
  | { readonly kind: 'suspended' }
  | { readonly kind: 'email-not-verified' }
  | { readonly kind: 'wrong-account-type' }
  | { readonly kind: 'rate-limited'; readonly retryAfterSeconds: number | null }
  | { readonly kind: 'unavailable' };

/**
 * The customer's session with the customer BFF. The browser holds only an HttpOnly cookie; the API
 * tokens stay in the BFF, which refreshes them and ends the session when the API disowns it.
 */
@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly http = inject(HttpClient);
  private readonly xsrf = inject(XsrfService);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  private readonly current = signal<SessionState>({ status: 'unknown' });
  private loading: Promise<SessionState> | null = null;

  readonly state = this.current.asReadonly();
  readonly user = computed(() => {
    const state = this.current();
    return state.status === 'signed-in' ? state.user : null;
  });
  readonly isSignedIn = computed(() => this.current().status === 'signed-in');
  readonly isResolved = computed(() => this.current().status !== 'unknown');

  /** Resolves the session once per page load. A no-op on the server. */
  resolve(): Promise<SessionState> {
    if (!this.isBrowser) return Promise.resolve(this.current());
    if (this.current().status !== 'unknown') return Promise.resolve(this.current());
    this.loading ??= firstValueFrom(this.http.get<SessionUser>('/bff/user'))
      .then((user): SessionState => ({ status: 'signed-in', user }))
      .catch((): SessionState => ({ status: 'anonymous' }))
      .then((state) => {
        this.current.set(state);
        this.loading = null;
        return state;
      });
    return this.loading;
  }

  async signIn(email: string, password: string): Promise<{ ok: true } | { ok: false; failure: SignInFailure }> {
    try {
      const user = await firstValueFrom(this.http.post<SessionUser>('/bff/login', { email, password }));
      this.xsrf.reset();
      this.current.set({ status: 'signed-in', user });
      return { ok: true };
    } catch (error) {
      this.xsrf.reset();
      return { ok: false, failure: classify(error) };
    }
  }

  async signOut(): Promise<void> {
    try {
      await firstValueFrom(this.http.post('/bff/logout', {}));
    } finally {
      this.xsrf.reset();
      this.current.set({ status: 'anonymous' });
    }
  }

  /**
   * The profile was just saved: show what the server returned. The BFF's copy of the name is the one
   * it recorded at sign-in and would otherwise go on greeting the customer by the old one.
   */
  updateUser(changes: Partial<Pick<SessionUser, 'fullName' | 'phone'>>): void {
    const state = this.current();
    if (state.status === 'signed-in') this.current.set({ status: 'signed-in', user: { ...state.user, ...changes } });
  }

  /** The BFF ended the session (the API answered 401): forget it here too. */
  markEnded(): void {
    this.xsrf.reset();
    this.current.set({ status: 'anonymous' });
  }
}

function classify(error: unknown): SignInFailure {
  if (!(error instanceof HttpErrorResponse)) return { kind: 'unavailable' };
  const code: string | undefined = error.error?.code;
  if (error.status === 401) return { kind: 'invalid-credentials' };
  if (error.status === 403) {
    if (code === 'auth.account_suspended') return { kind: 'suspended' };
    if (code === 'auth.email_not_verified') return { kind: 'email-not-verified' };
    if (code === 'bff.role_not_allowed') return { kind: 'wrong-account-type' };
    return { kind: 'unavailable' };
  }
  if (error.status === 429) {
    const header = error.headers.get('Retry-After');
    const seconds = header === null ? NaN : Number(header);
    return { kind: 'rate-limited', retryAfterSeconds: Number.isFinite(seconds) ? seconds : null };
  }
  return { kind: 'unavailable' };
}
