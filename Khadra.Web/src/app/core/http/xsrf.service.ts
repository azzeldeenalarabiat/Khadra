import { HttpBackend, HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/**
 * The antiforgery token the customer BFF demands on every write.
 *
 * Asked for once and reused, because the BFF binds it to the identity it was issued for: it must be
 * asked for again after signing in or out, which is what `reset()` is for. Uses the raw backend so
 * fetching it does not run the interceptor that is waiting for it.
 */
@Injectable({ providedIn: 'root' })
export class XsrfService {
  private readonly http = new HttpClient(inject(HttpBackend));
  private pending: Promise<string> | null = null;

  token(): Promise<string> {
    this.pending ??= firstValueFrom(
      this.http.get<{ requestToken: string }>('/bff/antiforgery', { withCredentials: true }),
    )
      .then((response) => response.requestToken)
      .catch((error: unknown) => {
        this.pending = null;
        throw error;
      });
    return this.pending;
  }

  reset(): void {
    this.pending = null;
  }
}
