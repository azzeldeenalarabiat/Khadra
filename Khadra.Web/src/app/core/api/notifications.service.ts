import { HttpClient } from '@angular/common/http';
import { DOCUMENT, Injectable, PLATFORM_ID, effect, inject, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { firstValueFrom } from 'rxjs';
import { SessionService } from '../session/session.service';

/** How often the unread count is asked for while the page is visible (docs/refresh-policy.md: 60s). */
const UNREAD_REFRESH_MS = 60_000;

export interface NotificationItem {
  readonly notificationId: string;
  readonly kind: string;
  readonly subjectId: string | null;
  readonly subjectReference: string | null;
  readonly actorName: string;
  readonly isMine: boolean;
  readonly occurredAt: string;
  readonly readAt: string | null;
  readonly isRead: boolean;
}

export interface NotificationFeed {
  readonly items: readonly NotificationItem[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly unreadCount: number;
}

/**
 * The unread count behind the header's bell: the backend's notifications, the same ones the app reads.
 * Asked for once signed in, then every minute while the page is visible, never while it is hidden.
 *
 * Structured so web push can later feed the same signal (a push arriving would call `refresh()`)
 * without any page changing.
 */
@Injectable({ providedIn: 'root' })
export class NotificationsService {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private readonly document = inject(DOCUMENT);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  readonly unread = signal(0);
  private timer: ReturnType<typeof setInterval> | null = null;

  constructor() {
    effect(() => {
      const signedIn = this.session.isSignedIn();
      if (!this.isBrowser) return;
      if (this.timer) clearInterval(this.timer);
      this.timer = null;
      if (!signedIn) {
        this.unread.set(0);
        return;
      }
      void this.refresh();
      this.timer = setInterval(() => {
        if (this.document.visibilityState === 'visible') void this.refresh();
      }, UNREAD_REFRESH_MS);
    });
  }

  async refresh(): Promise<void> {
    if (!this.session.isSignedIn()) return;
    try {
      this.unread.set(await firstValueFrom(this.http.get<number>('/api/v1/notifications/unread-count')));
    } catch {
      /* the badge keeps its last known count; the page itself reports failures */
    }
  }
}
