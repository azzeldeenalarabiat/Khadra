import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DealerActivityEntry } from '../../core/models/dealer-console.api';
import { Tone } from '../../core/models/console.models';
import { DealerConsoleService } from '../../core/services/dealer-console.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { I18nService } from '../../core/i18n/i18n.service';

/**
 * Activity (design `isActivity`): every status change on the dealership's bookings, newest first,
 * with the person who made it. Read straight from booking history -- there is no separate log to
 * drift from it.
 */
@Component({
  selector: 'kh-dealer-activity',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dealer-activity.component.html',
  imports: [RouterLink, IconComponent],
})
export class DealerActivityComponent {
  protected readonly t = inject(I18nService).t;
  // Server enum names, in the reader's language. Shared rather than per-component: the same enum
  // shows on half a dozen screens, and a copy each is a copy each to forget a new member in.
  protected readonly statusLabel = inject(I18nService).statusLabel;
  private readonly service = inject(DealerConsoleService);

  protected readonly page = this.service.activityPage;
  protected readonly resource = this.service.activity;
  /** Guarded: `value()` throws in the error state, so nothing reads the resource directly. */
  private readonly data = loaded(this.resource);
  protected readonly entries = computed(() => this.data()?.items ?? []);
  protected readonly total = computed(() => this.data()?.totalCount ?? 0);

  /** One change is a change, not "1 changes". */
  protected readonly summary = computed(() => {
    const total = this.total();
    return `${this.entries().length} of ${total} ${total === 1 ? 'change' : 'changes'}`;
  });
  protected readonly totalPages = computed(() => this.data()?.totalPages ?? 1);

  protected readonly failure = computed(() =>
    this.resource.error() ? this.t('dealerActivity.activityCouldNotBe') : null,
  );

  protected goTo(page: number): void {
    this.page.set(Math.max(1, page));
  }

  protected tone(e: DealerActivityEntry): Tone {
    switch (e.toStatus) {
      case 'Rejected':
      case 'Cancelled':
      case 'NoShow':
        return 'bad';
      // Both of the states with a clock running on them: nobody has answered, or nobody has paid.
      case 'Requested':
      case 'Approved':
        return 'warn';
      case 'Confirmed':
      case 'PickedUp':
      case 'Returned':
      case 'Completed':
        return 'ok';
      default:
        return 'dim';
    }
  }

  protected describe(e: DealerActivityEntry): string {
    const labels: Record<string, string> = {
      Requested: this.t('dealerActivity.requestedAwaitingYourAnswer'),
      Approved: this.t('dealerActivity.approvedAwaitingTheDeposit'),
      Confirmed: this.t('dealerActivity.depositPaidBookingConfirmed'),
      Rejected: 'Rejected',
      PickedUp: this.t('status.pickedUp'),
      Returned: 'Returned',
      Completed: 'Completed',
      Cancelled: 'Cancelled',
      NoShow: this.t('dealerActivity.markedNoShow'),
      Expired: this.t('dealerActivity.expiredUnanswered'),
    };
    return labels[e.toStatus] ?? e.toStatus;
  }

  protected when(iso: string): string {
    return new Date(iso).toLocaleString('en-GB', {
      day: '2-digit',
      month: 'short',
      hour: '2-digit',
      minute: '2-digit',
    });
  }
}
