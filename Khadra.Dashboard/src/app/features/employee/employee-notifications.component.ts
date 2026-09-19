import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Tone } from '../../core/models/console.models';
import { NotificationItem } from '../../core/models/notifications.api';
import { NotificationsService } from '../../core/services/notifications.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { loaded } from '../../core/services/loaded';
import { IconName } from '../../shared/icon/icon-paths';
import { IconComponent } from '../../shared/icon/icon.component';
import { I18nService } from '../../core/i18n/i18n.service';
import { FormatService } from '../../core/i18n/format.service';
import { serverSentence, snapshotProblem } from '../../core/i18n/problem';

/**
 * Notifications (design: Employee Console, `isNotifications`).
 *
 * Every row is a real notification from `/api/v1/notifications` — written into the same transaction
 * as the action it describes, so a booking cannot be approved with nobody told. Nothing here is
 * derived from live bookings: that is what the dashboard's "Requires attention" panel does, and the
 * two answer different questions. This one is "what happened while I was away", and it keeps read
 * state, which work-in-progress cannot.
 *
 * What raises rows today is the DEALERSHIP acting on itself: a colleague answering a request or
 * handing a car over, the platform approving or suspending the business, a change to someone's own
 * access. What does not raise rows — a customer's request arriving, a customer cancelling, a pickup
 * falling due — has no producer in this repository: the customer flow is the Flutter app and there is
 * no scheduler. Those kinds are absent from the server's vocabulary rather than present and
 * permanently empty, so nothing here promises an alert that cannot come.
 */
@Component({
  selector: 'kh-employee-notifications',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './employee-notifications.component.html',
  imports: [IconComponent, RouterLink],
})
export class EmployeeNotificationsComponent {
  private readonly i18n = inject(I18nService);
  protected readonly t = this.i18n.t;
  protected readonly formats = inject(FormatService);
  private readonly service = inject(NotificationsService);
  private readonly ui = inject(ConsoleUiService);

  protected readonly resource = this.service.feed;
  /** Guarded: `value()` throws in the error state, so nothing reads the resource directly. */
  private readonly data = loaded(this.resource);

  protected readonly items = computed(() => this.data()?.items ?? []);
  protected readonly unread = computed(() => this.data()?.unreadCount ?? 0);
  protected readonly total = computed(() => this.data()?.totalCount ?? 0);
  protected readonly busy = signal(false);

  protected readonly failure = computed(() => {
    const error = this.resource.error() as { status?: number } | undefined;
    if (!error) return null;
    return this.t('employeeNotif.yourNotificationsCouldNot');
  });

  protected describe(item: NotificationItem): string {
    return this.service.describe(item);
  }

  protected routeFor(item: NotificationItem): readonly string[] | null {
    return this.service.routeFor(item, 'employee');
  }

  /** The badge on a row: what KIND of thing happened, in one word. */
  protected label(item: NotificationItem): string {
    switch (item.kind) {
      case 'BookingRequested':
        return this.t('employeeNotif.newRequest');
      case 'BookingApproved':
      case 'BookingRejected':
        return this.t('employeeNotif.bookingDecision');
      case 'BookingPickedUp':
        return this.t('handoverType.pickup');
      case 'BookingReturned':
        return this.t('handoverType.return');
      case 'ReportAccessGranted':
      case 'ReportAccessRevoked':
        return this.t('employeeNotif.yourAccess');
      case 'StaffReactivated':
        return this.t('employeeNotif.team');
      default:
        return this.t('employeeNotif.dealership');
    }
  }

  protected tone(item: NotificationItem): Tone {
    switch (item.kind) {
      case 'BookingRejected':
      case 'DealerSuspended':
      case 'DealerRejected':
      case 'ReportAccessRevoked':
        return 'bad';
      case 'DealerClarificationRequested':
      // A request is somebody waiting on this dealership, with a clock running. It is the one
      // notification here that is a task rather than a record of one.
      case 'BookingRequested':
        return 'warn';
      case 'BookingApproved':
      case 'BookingReturned':
      case 'DealerApproved':
      case 'DealerReactivated':
      case 'ReportAccessGranted':
        return 'ok';
      default:
        return 'accent';
    }
  }

  protected icon(item: NotificationItem): IconName {
    switch (item.kind) {
      case 'BookingRequested':
        return 'bell-ringing';
      case 'BookingApproved':
        return 'check-circle';
      case 'BookingRejected':
        return 'x-circle';
      case 'BookingPickedUp':
      case 'BookingReturned':
        return 'key';
      case 'ReportAccessGranted':
      case 'ReportAccessRevoked':
        return 'chart-line-up';
      case 'StaffReactivated':
        return 'users-three';
      default:
        return 'storefront';
    }
  }

  protected async open(item: NotificationItem): Promise<void> {
    if (item.isRead) return;
    await this.service.markRead(item.notificationId);
  }

  protected async markAllRead(): Promise<void> {
    if (this.busy() || this.unread() === 0) return;
    this.busy.set(true);
    try {
      const changed = await this.service.markAllRead();
      this.ui.showToast(
        this.t('employeeNotif.markedAsRead'),
        // A plural message: one notification is not "1 notifications", in either language.
        this.t('employeeNotif.notificationsMarkedRead', { count: changed }),
      );
    } catch (error) {
      // Worded as it is shown, in the language on screen: a toast is gone in seconds, so there is no
      // refusal left standing to re-word on a switch. The server's English only ever reads in English.
      this.ui.showToast(
        this.t('common.thatDidNotGoThrough'),
        serverSentence(snapshotProblem(error), this.i18n.lang(), this.t) ??
          this.t('common.serviceDidNotRespond'),
        'bad',
      );
    } finally {
      this.busy.set(false);
    }
  }
}
