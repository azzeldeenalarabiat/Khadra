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
  /**
   * Whether their invitation is still open — nobody has chosen a password on the account.
   *
   * Not the negation of `isEmailVerified`: an invited administrator can prove their address
   * through resend-verification and still hold no password, and that is the person who most needs
   * the link sent again.
   */
  readonly invitationPending: boolean;
}

export interface InviteAdminResult {
  readonly userId: string;
  readonly email: string;
  /** The token's own expiry, so the console never states a lifetime it guessed. */
  readonly expiresAt: string;
  /**
   * Whether the relay ACCEPTED the invitation email. The API has always sent this; the console
   * used to drop it and say "Invitation sent" either way.
   *
   * False does not undo the invitation — the account and its token stand — but nobody else can
   * find out: the row looks identical, there is no resend, and the unique index on the address
   * means re-inviting answers 409 for ever. So the one person who can act on it is told.
   */
  readonly invitationEmailSent: boolean;
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

  /**
   * A second invitation email, with a fresh link. Every earlier link for that account dies.
   *
   * Unlike `invite`, this FAILS when the relay refuses the message (503): it exists to deliver
   * one, and reporting success would send the administrator back to the same button.
   */
  resendInvitation(userId: string): Promise<InviteAdminResult> {
    return this.withToken((token) =>
      firstValueFrom(
        this.http.post<InviteAdminResult>(
          `${this.base}/${userId}/resend-invitation`,
          {},
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
