import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DealerActivityEntry } from '../../core/models/dealer-console.api';
import { Tone } from '../../core/models/console.models';
import { DealerConsoleService } from '../../core/services/dealer-console.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { TranslationKey } from '../../core/i18n/en';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';

/**
 * The changes this screen words as more than a status's name: who is waiting on whom, or what
 * happened. Every other status goes through `statusLabel`, so a status the domain adds later still
 * reads as something rather than as its identifier.
 */
const DESCRIPTIONS: Readonly<Record<string, TranslationKey>> = {
  Requested: 'dealerActivity.requestedAwaitingYourAnswer',
  Approved: 'dealerActivity.approvedAwaitingTheDeposit',
  Confirmed: 'dealerActivity.depositPaidBookingConfirmed',
  PickedUp: 'status.pickedUp',
  NoShow: 'dealerActivity.markedNoShow',
  Expired: 'dealerActivity.expiredUnanswered',
};

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
  private readonly formats = inject(FormatService);
  private readonly service = inject(DealerConsoleService);

  protected readonly page = this.service.activityPage;
  protected readonly resource = this.service.activity;
  /** Guarded: `value()` throws in the error state, so nothing reads the resource directly. */
  private readonly data = loaded(this.resource);
  protected readonly entries = computed(() => this.data()?.items ?? []);
  protected readonly total = computed(() => this.data()?.totalCount ?? 0);

  /** One change is a change, not "1 changes": the total picks the noun's form, in either language. */
  protected readonly summary = computed(() =>
    this.t('dealerActivity.pageSummary', { shown: this.entries().length, count: this.total() }),
  );
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
    const key = DESCRIPTIONS[e.toStatus];
    // The dealer's own wording for everything else, the same the bookings list uses.
    return key ? this.t(key) : this.statusLabel(e.toStatus, 'dealerBooking');
  }

  /**
   * Who made the change, as the API names them: a change recorded against no user is the rental
   * office's own, and a user with no name is somebody whose account has since been closed.
   */
  protected actor(e: DealerActivityEntry): string {
    if (e.actorUserId === null) return this.t('common.theRentalOffice');
    return e.actorName ?? this.t('common.formerStaffMember');
  }

  /** "06 Sept, 14:32", in the reader's language. */
  protected when(iso: string): string {
    return this.formats.dayMonthTime(iso);
  }
}
