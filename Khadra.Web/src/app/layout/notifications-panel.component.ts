import { ChangeDetectionStrategy, Component, inject, output, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { NotificationItem, NotificationsService } from '../core/api/notifications.service';
import { ProblemSnapshot, snapshotProblem } from '../core/http/problem';
import { FormatService } from '../core/i18n/format.service';
import { I18nService } from '../core/i18n/i18n.service';
import { notificationTarget } from '../features/notifications/notification-target';
import { notificationAbout, notificationText } from '../features/notifications/notification-text';
import { IconComponent } from '../shared/icon/icon.component';

/** How many of the newest notifications the header's panel shows before "view all". Layout only. */
export const PANEL_SIZE = 6;

/**
 * The bell's panel: the newest notifications at a glance, without leaving the page. A popover on a
 * desktop, a sheet from the bottom on a phone (the stylesheet decides which). It is created when the
 * bell opens it and destroyed when it closes, so every opening reads the feed fresh.
 *
 * Opening an item marks it read, goes to its booking or dispute, and closes the panel. "View all" is
 * the full notifications page, which stays the place for history and paging.
 */
@Component({
  selector: 'kh-notifications-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, IconComponent],
  templateUrl: './notifications-panel.component.html',
})
export class NotificationsPanelComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly format = inject(FormatService);
  private readonly router = inject(Router);
  private readonly notifications = inject(NotificationsService);

  /** Asks the header to close the panel (an item was opened, or "view all" was followed). */
  readonly closed = output<void>();

  protected readonly items = signal<NotificationItem[] | null>(null);
  protected readonly unread = signal(0);
  protected readonly hasMore = signal(false);
  protected readonly problem = signal<ProblemSnapshot | null>(null);

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.problem.set(null);
    try {
      const feed = await this.notifications.feed(1, PANEL_SIZE);
      this.items.set([...feed.items]);
      this.unread.set(feed.unreadCount);
      this.hasMore.set(feed.totalCount > feed.items.length);
    } catch (error) {
      this.problem.set(snapshotProblem(error));
    }
  }

  protected text(item: NotificationItem): string {
    return notificationText(this.i18n, item);
  }

  protected about(item: NotificationItem): string {
    return notificationAbout(this.i18n, item);
  }

  protected open(item: NotificationItem): void {
    if (!item.isRead) {
      this.items.update((current) => current?.map((entry) => (entry === item ? { ...entry, isRead: true } : entry)) ?? null);
      this.unread.update((count) => Math.max(0, count - 1));
      void this.notifications.markRead(item.notificationId).catch(() => undefined);
    }
    const target = notificationTarget(item.kind, item.subjectId);
    if (target) void this.router.navigate(this.i18n.link(...target));
    this.closed.emit();
  }

  protected async markAll(): Promise<void> {
    try {
      await this.notifications.markAllRead();
      this.items.update((current) => current?.map((entry) => ({ ...entry, isRead: true })) ?? null);
      this.unread.set(0);
    } catch (error) {
      this.problem.set(snapshotProblem(error));
    }
  }
}
