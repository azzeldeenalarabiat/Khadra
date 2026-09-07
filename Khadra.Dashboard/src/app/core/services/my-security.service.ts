import { HttpClient, httpResource } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** One sign-in: a whole token family, not a row per refresh. */
export interface SessionSummary {
  readonly familyId: string;
  readonly signedInAt: string;
  readonly lastUsedAt: string;
  readonly expiresAt: string;
  readonly createdByIp: string | null;
  readonly userAgent: string | null;
  readonly isActive: boolean;
}

export interface MySessionsView {
  readonly sessions: readonly SessionSummary[];
  /**
   * How long a revoked session can still make requests. Revoking stops the refresh, not the access
   * token already issued, and the screen says so rather than promising immediacy it cannot deliver.
   */
  readonly accessTokenMinutes: number;
}

/** The signed-in person's own account. Never addressable by another user's id. */
@Injectable({ providedIn: 'root' })
export class MySecurityService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/auth/sessions';

  readonly sessions = httpResource<MySessionsView>(() => this.base);

  async revoke(familyId: string): Promise<void> {
    const token = await firstValueFrom(this.http.get<{ requestToken: string }>('/bff/antiforgery'));
    await firstValueFrom(
      this.http.post<void>(
        `${this.base}/${familyId}/revoke`,
        {},
        { headers: { 'X-XSRF-TOKEN': token.requestToken } },
      ),
    );
  }

  /** Changes the password and, server-side, ends every other session. */
  async changePassword(currentPassword: string, newPassword: string): Promise<void> {
    const token = await firstValueFrom(this.http.get<{ requestToken: string }>('/bff/antiforgery'));
    await firstValueFrom(
      this.http.post<void>(
        '/api/v1/auth/change-password',
        { currentPassword, newPassword },
        { headers: { 'X-XSRF-TOKEN': token.requestToken } },
      ),
    );
  }

  refresh(): void {
    this.sessions.reload();
  }
}
