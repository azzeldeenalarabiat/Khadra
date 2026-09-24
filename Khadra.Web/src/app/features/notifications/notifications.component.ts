import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { NotificationItem, NotificationsService } from '../../core/api/notifications.service';
import { ProblemSnapshot, snapshotProblem } from '../../core/http/problem';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { SeoService } from '../../core/seo/seo.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { StatePanelComponent } from '../../shared/state/state-panel.component';
import { notificationTarget } from './notification-target';
import { notificationAbout, notificationText } from './notification-text';

export { KNOWN_KINDS } from './notification-text';

const PAGE_SIZE = 20;

/**
 * The customer's notifications, from the backend — the same feed the app shows, and the "view all"
 * behind the header's panel. Opening one marks it read and goes to what it is about: its booking, or —
 * for a dispute update — the dispute.
 */
@Component({
  selector: 'kh-notifications',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [IconComponent, StatePanelComponent],
  templateUrl: './notifications.component.html',
})
export class NotificationsComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly format = inject(FormatService);
  private readonly router = inject(Router);
  private readonly notifications = inject(NotificationsService);

  protected readonly items = signal<NotificationItem[]>([]);
  protected readonly loaded = signal(false);
  protected readonly hasMore = signal(false);
  protected readonly unread = signal(0);
  protected readonly loading = signal(false);
  protected readonly problem = signal<ProblemSnapshot | null>(null);
  private page = 0;

  constructor() {
    inject(SeoService).set({ title: this.i18n.t('seo.notifications.title'), noindex: true });
    void this.more();
  }

  protected async more(): Promise<void> {
    if (this.loading()) return;
    this.loading.set(true);
    this.problem.set(null);
    try {
      const feed = await this.notifications.feed(this.page + 1, PAGE_SIZE);
      this.page = feed.page;
      this.items.update((current) => [...current, ...feed.items]);
      this.unread.set(feed.unreadCount);
      this.hasMore.set(feed.page * feed.pageSize < feed.totalCount);
      this.loaded.set(true);
    } catch (error) {
      this.problem.set(snapshotProblem(error));
    } finally {
      this.loading.set(false);
    }
  }

  protected retry(): void {
    this.page = 0;
    this.items.set([]);
    void this.more();
  }

  protected text(item: NotificationItem): string {
    return notificationText(this.i18n, item);
  }

  protected about(item: NotificationItem): string {
    return notificationAbout(this.i18n, item);
  }

  protected open(item: NotificationItem): void {
    if (!item.isRead) {
      this.items.update((current) => current.map((entry) => (entry === item ? { ...entry, isRead: true } : entry)));
      this.unread.update((count) => Math.max(0, count - 1));
      void this.notifications.markRead(item.notificationId).catch(() => undefined);
    }
    // Every customer kind is about a booking, whose id is the subject — except a dispute update,
    // whose subject is the dispute ticket (as in the app).
    const target = notificationTarget(item.kind, item.subjectId);
    if (target) void this.router.navigate(this.i18n.link(...target));
  }

  protected async markAll(): Promise<void> {
    try {
      await this.notifications.markAllRead();
      this.items.update((current) => current.map((entry) => ({ ...entry, isRead: true })));
      this.unread.set(0);
    } catch (error) {
      this.problem.set(snapshotProblem(error));
    }
  }
}
