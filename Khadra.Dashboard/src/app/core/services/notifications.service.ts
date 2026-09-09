import { HttpClient, httpResource } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter, firstValueFrom, map, startWith } from 'rxjs';
import { NotificationFeed, NotificationItem } from '../models/notifications.api';
import { SessionService } from './session.service';
import { I18nService } from '../i18n/i18n.service';

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

  private readonly navigation = toSignal(
    inject(Router).events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map((_, index) => index + 1),
      startWith(0),
    ),
    { initialValue: 0 },
  );

  readonly page = signal(1);

  readonly feed = httpResource<NotificationFeed>(() => {
    // Read so the resource re-runs on navigation; the value is not part of the request.
    this.navigation();
    if (!this.signedIn()) return undefined;
    return { url: this.base, params: { page: this.page(), pageSize: 25 } };
  });

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

  /**
   * The sentence for a row, composed here rather than stored.
   *
   * Every kind in the server's vocabulary has a case; an unknown one falls back to something true
   * rather than to an empty line, because a new kind should degrade, not disappear.
   */
  describe(item: NotificationItem): string {
    // Named PARAMETERS rather than a template literal, and that is the whole reason this could not
    // be keyed by codemod: Arabic does not put the actor and the object where English puts them, so
    // the sentence has to be one message with two holes in it, not three pieces concatenated.
    const who = item.isMine ? this.t('notifications.you') : item.actorName;
    const what = item.subjectReference ?? this.t('notifications.aBooking');
    const parts = { who, what };

    switch (item.kind) {
      // The one kind raised from outside the dealership. Its row carries no actor on purpose --
      // a customer's name is never copied into this table -- so it does not use `who`.
      case 'BookingRequested':
        return this.t('notifications.customerRequested', { what });
      case 'BookingApproved':
        return this.t('notifications.approved', parts);
      case 'BookingRejected':
        return this.t('notifications.rejected', parts);
      case 'BookingPickedUp':
        return this.t('notifications.recordedPickup', parts);
      case 'BookingReturned':
        return this.t('notifications.recordedReturn', parts);
      case 'BookingConfirmed':
        return this.t('notifications.customerPaid', { what });
      case 'DealerApproved':
        return this.t('notifications.dealerApproved');
      case 'DealerRejected':
        return this.t('notifications.dealerRejected');
      case 'DealerClarificationRequested':
        return this.t('notifications.dealerClarification');
      case 'DealerSuspended':
        return this.t('notifications.dealerSuspended');
      case 'DealerReactivated':
        return this.t('notifications.dealerReactivated');
      case 'StaffReactivated':
        return this.t('notifications.staffReactivated', { who });
      case 'ReportAccessGranted':
        return this.t('notifications.reportAccessGranted', { who });
      case 'ReportAccessRevoked':
        return this.t('notifications.reportAccessRevoked', { who });
      default:
        return this.t('notifications.updated', parts);
    }
  }

  /** Where a row leads, for the console the reader is standing in. */
  routeFor(item: NotificationItem, area: 'employee' | 'dealer'): readonly string[] | null {
    if (!item.subjectId) return null;

    switch (item.kind) {
      case 'BookingRequested':
      case 'BookingApproved':
      case 'BookingRejected':
      case 'BookingPickedUp':
      case 'BookingReturned':
        return [`/${area}/bookings`, item.subjectId];
      // The dealership kinds point at the dealership itself, which an employee has no screen for
      // beyond the read-only one; the row says what happened and that is the whole of it.
      default:
        return null;
    }
  }
}
