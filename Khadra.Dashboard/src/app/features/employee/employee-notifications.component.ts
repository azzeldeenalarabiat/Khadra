import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
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
 * Notifications (design: Employee Console, `isNotifications`), for the owner as for staff: the owner's page was
 * a placeholder over rows that existed (Wave 3, F28), and `area` in the route's data is which console it is in.
 *
 * Every row is a real notification from `/api/v1/notifications` — written into the same transaction
 * as the action it describes, so a booking cannot be approved with nobody told. Nothing here is
 * derived from live bookings: that is what the dashboard's "Requires attention" panel does, and the
 * two answer different questions. This one is "what happened while I was away", and it keeps read
 * state, which work-in-progress cannot.
 *
 * What raises rows: a colleague answering a request or handing a car over; a customer requesting,
 * paying for, cancelling or disputing a booking, or reporting a car not handed over; the platform
 * deciding about the business, completing a booking, marking a no-show, expiring an approval nobody
 * paid for, cancelling, deciding a dispute or settling the office's payouts; a change to someone's own
 * access. A request expiring unanswered and a pickup falling due raise nothing here (Wave 3, C7): the
 * dashboard and the bookings work those out live.
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

  /** Which console this page is in: the owner's (`/dealer`) or an employee's (`/employee`). */
  protected readonly area: 'dealer' | 'employee' =
    inject(ActivatedRoute).snapshot.data['area'] === 'dealer' ? 'dealer' : 'employee';

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
    return this.service.routeFor(item, this.area);
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
      case 'BookingConfirmed':
      case 'BookingCancelledByCustomer':
      case 'BookingNonDeliveryReported':
      case 'BookingCompleted':
      case 'BookingMarkedNoShow':
      case 'BookingExpiredUnpaid':
      case 'BookingCancelledByAdmin':
        return this.t('employeeNotif.booking');
      case 'DisputeOpened':
      case 'DisputeResolved':
        return this.t('employeeNotif.dispute');
      case 'SettlementRecorded':
      case 'SettlementVoided':
        return this.t('employeeNotif.payout');
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
      case 'BookingCancelledByCustomer':
      case 'BookingNonDeliveryReported':
      case 'BookingMarkedNoShow':
      case 'BookingCancelledByAdmin':
        return 'bad';
      case 'DealerClarificationRequested':
      // A request is somebody waiting on this dealership, with a clock running. A dispute waits on
      // the office's own statement. Both are tasks rather than records of one.
      case 'BookingRequested':
      case 'DisputeOpened':
      case 'SettlementVoided':
        return 'warn';
      case 'BookingApproved':
      case 'BookingReturned':
      case 'BookingConfirmed':
      case 'BookingCompleted':
      case 'DealerApproved':
      case 'DealerReactivated':
      case 'ReportAccessGranted':
      case 'SettlementRecorded':
        return 'ok';
      case 'BookingExpiredUnpaid':
        return 'dim';
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
      case 'BookingConfirmed':
      case 'BookingCompleted':
        return 'check-circle';
      case 'BookingCancelledByCustomer':
      case 'BookingNonDeliveryReported':
      case 'BookingMarkedNoShow':
      case 'BookingCancelledByAdmin':
        return 'x-circle';
      case 'BookingExpiredUnpaid':
        return 'clock';
      case 'DisputeOpened':
      case 'DisputeResolved':
        return 'scales';
      case 'SettlementRecorded':
      case 'SettlementVoided':
        return 'currency-circle-dollar';
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
