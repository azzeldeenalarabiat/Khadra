import { HttpClient } from '@angular/common/http';
import { DOCUMENT, Injectable, PLATFORM_ID, effect, inject, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { firstValueFrom } from 'rxjs';
import { ConsentGateService } from '../session/consent-gate.service';
import { SessionService } from '../session/session.service';

/** How often the unread count is asked for while the page is visible (docs/refresh-policy.md: 60s). */
const UNREAD_REFRESH_MS = 60_000;

export interface NotificationItem {
  readonly notificationId: string;
  readonly kind: string;
  readonly subjectId: string | null;
  readonly subjectReference: string | null;
  readonly actorName: string;
  /**
   * Set when the actor is a stand-in rather than a name — `RentalOffice` for an office that has left the platform —
   * and `actorName` then holds the English phrase (pre-launch item 103). Absent on rows written before 2026-10-08.
   */
  readonly actorStandIn?: string | null;
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
  private readonly consent = inject(ConsentGateService);
  private readonly document = inject(DOCUMENT);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  readonly unread = signal(0);
  private timer: ReturnType<typeof setInterval> | null = null;
  /**
   * Bumped by every read of the count and every change to it. An answer that arrives after a newer
   * one was asked for, or after the visitor marked something read, is older than what the badge
   * already knows and is dropped — otherwise a minute-old poll could put a cleared count back.
   */
  private generation = 0;

  constructor() {
    effect(() => {
      const signedIn = this.session.isSignedIn();
      // Silent until the consent answer is in and behind the prompt (Wave 4, W4-8), where every answer would be a
      // refusal; asked, and polled, the moment the texts are known to be accepted.
      const open = this.consent.open();
      if (!this.isBrowser) return;
      if (this.timer) clearInterval(this.timer);
      this.timer = null;
      if (!signedIn) {
        this.unread.set(0);
        return;
      }
      if (!open) return;
      void this.refresh();
      this.timer = setInterval(() => {
        if (this.document.visibilityState === 'visible') void this.refresh();
      }, UNREAD_REFRESH_MS);
    });
  }

  /** One page of the feed, newest first. */
  feed(page: number, pageSize: number): Promise<NotificationFeed> {
    return firstValueFrom(this.http.get<NotificationFeed>('/api/v1/notifications', { params: { page, pageSize } }));
  }

  /**
   * Marks one read. The badge drops at once, with the row the visitor just opened, then settles on the
   * server's count; if the call fails the server's count puts it back.
   */
  async markRead(notificationId: string): Promise<void> {
    this.generation++;
    this.unread.update((count) => Math.max(0, count - 1));
    try {
      await firstValueFrom(this.http.post(`/api/v1/notifications/${notificationId}/read`, {}));
    } finally {
      void this.refresh();
    }
  }

  async markAllRead(): Promise<void> {
    await firstValueFrom(this.http.post('/api/v1/notifications/read-all', {}));
    this.generation++;
    this.unread.set(0);
    void this.refresh();
  }

  async refresh(): Promise<void> {
    if (!this.session.isSignedIn() || !this.consent.open()) return;
    const asked = ++this.generation;
    try {
      const count = await firstValueFrom(this.http.get<number>('/api/v1/notifications/unread-count'));
      if (asked === this.generation) this.unread.set(count);
    } catch {
      /* the badge keeps its last known count; the page itself reports failures */
    }
  }
}
