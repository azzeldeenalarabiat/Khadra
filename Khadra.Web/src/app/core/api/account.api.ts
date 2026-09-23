/** `UserDto` from `GET /api/v1/auth/me` and `PUT /customers/me/profile`. */
export interface AccountUser {
  readonly id: string;
  readonly email: string;
  readonly fullName: string;
  /** Normalised by the server (`07…` comes back as `+9627…`); show this, not what was typed. */
  readonly phone: string;
  readonly role: string;
  readonly isEmailVerified: boolean;
  readonly mustChangePassword: boolean;
  readonly createdAt: string;
}

/** `GET /api/v1/auth/sessions`. */
export interface AccountSessions {
  readonly sessions: readonly AccountSession[];
  readonly accessTokenMinutes: number;
}

export interface AccountSession {
  readonly familyId: string;
  readonly signedInAt: string;
  readonly lastUsedAt: string | null;
  readonly expiresAt: string;
  readonly createdByIp: string | null;
  readonly userAgent: string | null;
  readonly isActive: boolean;
  readonly isCurrent: boolean;
}
