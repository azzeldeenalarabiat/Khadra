import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DealerActivityEntry } from '../../core/models/dealer-console.api';
import { Tone } from '../../core/models/console.models';
import { DealerConsoleService } from '../../core/services/dealer-console.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import {
  ActivityWords,
  activityActor,
  activityEvent,
  activityReason,
} from './booking-activity.presenter';

/**
 * Activity (design `isActivity`): every status change on the dealership's bookings, newest first,
 * with who made it — the office's staff, the customer or Khadra (Wave 3, F27: it listed only the
 * office's own, under a heading that promised every change). Read straight from booking history --
 * there is no separate log to drift from it. The words are `booking-activity.presenter`'s, which the
 * dashboard, a car's log and the booking page share.
 */
@Component({
  selector: 'kh-dealer-activity',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dealer-activity.component.html',
  imports: [RouterLink, IconComponent],
})
export class DealerActivityComponent {
  private readonly i18n = inject(I18nService);
  protected readonly t = this.i18n.t;
  // Server enum names, in the reader's language. Shared rather than per-component: the same enum
  // shows on half a dozen screens, and a copy each is a copy each to forget a new member in.
  protected readonly statusLabel = this.i18n.statusLabel;
  private readonly formats = inject(FormatService);
  private readonly service = inject(DealerConsoleService);
  private readonly words: ActivityWords = {
    t: this.t,
    status: this.statusLabel,
    party: (name) => this.i18n.enumLabel('party', name),
  };

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

  /** What happened, in the past tense: a line of history, never what the booking waits for now (F23). */
  protected describe(e: DealerActivityEntry): string {
    return activityEvent(e, this.words);
  }

  /** The member of staff, the rental office, the customer or Khadra. */
  protected actor(e: DealerActivityEntry): string {
    return activityActor(e, this.words);
  }

  /** What somebody typed, quoted; the platform's own English on an expiry is not shown. */
  protected reason(e: DealerActivityEntry): string | null {
    return activityReason(e);
  }

  /** "06 Sept, 14:32", in the reader's language. */
  protected when(iso: string): string {
    return this.formats.dayMonthTime(iso);
  }
}
