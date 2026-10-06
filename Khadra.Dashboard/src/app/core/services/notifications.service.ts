import { HttpClient, httpResource } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { NotificationFeed, NotificationItem } from '../models/notifications.api';
import { LiveRefreshService } from './live-refresh.service';
import { liveResource } from './live-surface';
import { SessionService } from './session.service';
import { I18nService } from '../i18n/i18n.service';
import { notificationRoute, notificationSentence } from './notifications.presenter';

/**
 * The signed-in person's notifications.
 *
 * ONE resource feeds the screen, the rail badge and the bell, so the count can never disagree with
 * the list — the specific way a notifications UI goes wrong, and the reason the old bell refused to
 * show a number at all.
 *
 * Re-read on every completed navigation, the same cadence the rail's other counts use. A booking
 * approved by a colleague while you are on another screen should be waiting for you when you get
 * back, not on the next full reload.
 */
@Injectable({ providedIn: 'root' })
export class NotificationsService {
  private readonly t = inject(I18nService).t;
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private readonly base = '/api/v1/notifications';

  /** Anyone signed in has a bell. What fills it differs by what raises rows, not by role. */
  private readonly signedIn = computed(() => !!this.session.user());

  private readonly live = inject(LiveRefreshService);

  readonly page = signal(1);

  /**
   * The bell, and the list behind it.
   *
   * It used to re-run its PARAMS on every completed navigation, which fetched twenty-five rows per
   * screen change AND dropped the badge to null each time — a params run builds a new request
   * object, and a resource only keeps its value while the reference holds. Now it polls on a minute
   * and re-reads on route entry past the floor, through `reload()`, which keeps the number on screen
   * while the new one is on its way.
   */
  readonly feed = httpResource<NotificationFeed>(() => {
    if (!this.signedIn()) return undefined;
    return { url: this.base, params: { page: this.page(), pageSize: 25 } };
  });

  constructor() {
    liveResource(this.live, this.feed, {
      id: 'notifications',
      tier: 'live',
      pollSeconds: 60,
      // The bell is on every screen, so it is always the surface in front of somebody.
      visible: () => this.signedIn(),
    });
  }

  /** Null until the feed answers — a badge that guesses zero claims an empty inbox nobody checked. */
  readonly unreadCount = computed(() =>
    this.feed.hasValue() ? this.feed.value().unreadCount : null,
  );

  async markRead(notificationId: string): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/${notificationId}/read`, {}));
    this.feed.reload();
  }

  /** Answers how many changed, so the caller can say something true in its toast. */
  async markAllRead(): Promise<number> {
    const changed = await firstValueFrom(this.http.post<number>(`${this.base}/read-all`, {}));
    this.feed.reload();
    return changed;
  }

  /** The sentence for a row, composed rather than stored: `notificationSentence`. */
  describe(item: NotificationItem): string {
    return notificationSentence(item, this.t);
  }

  /** Where a row leads, for the console the reader is standing in. */
  routeFor(item: NotificationItem, area: 'employee' | 'dealer'): readonly string[] | null {
    return notificationRoute(item, area);
  }
}
