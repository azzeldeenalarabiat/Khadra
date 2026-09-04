import { HttpClient, httpResource } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** An administrator, as `/api/v1/admin/admin-users` lists them. */
export interface AdminUserListItem {
  readonly userId: string;
  readonly fullName: string;
  readonly email: string;
  readonly phone: string;
  readonly status: 'Active' | 'Suspended';
  readonly isEmailVerified: boolean;
  readonly lastLoginAt: string | null;
  readonly createdAt: string;
  readonly suspensionReason: string | null;
  /** Entries in the append-only trail attributed to them. */
  readonly auditedActions: number;
}

export interface InviteAdminResult {
  readonly userId: string;
  readonly email: string;
  /** The token's own expiry, so the console never states a lifetime it guessed. */
  readonly expiresAt: string;
}

@Injectable({ providedIn: 'root' })
export class AdminUsersService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/admin/admin-users';

  readonly list = httpResource<readonly AdminUserListItem[]>(() => this.base);

  private async withToken<T>(call: (token: string) => Promise<T>): Promise<T> {
    const token = await firstValueFrom(this.http.get<{ requestToken: string }>('/bff/antiforgery'));
    return call(token.requestToken);
  }

  invite(email: string, phone: string, fullName: string): Promise<InviteAdminResult> {
    return this.withToken((token) =>
      firstValueFrom(
        this.http.post<InviteAdminResult>(
          this.base,
          { email, phone, fullName },
          { headers: { 'X-XSRF-TOKEN': token } },
        ),
      ),
    );
  }

  deactivate(userId: string, reason: string): Promise<void> {
    return this.withToken((token) =>
      firstValueFrom(
        this.http.post<void>(
          `${this.base}/${userId}/deactivate`,
          { reason },
          { headers: { 'X-XSRF-TOKEN': token } },
        ),
      ),
    );
  }

  reactivate(userId: string): Promise<void> {
    return this.withToken((token) =>
      firstValueFrom(
        this.http.post<void>(
          `${this.base}/${userId}/reactivate`,
          {},
          { headers: { 'X-XSRF-TOKEN': token } },
        ),
      ),
    );
  }

  refresh(): void {
    this.list.reload();
  }
}
