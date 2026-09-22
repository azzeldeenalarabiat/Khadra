import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { Tone } from '../../core/models/console.models';
import { BookingListItem } from '../../core/models/bookings.api';
import { BookingTab, DealerBookingsService } from '../../core/services/dealer-bookings.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { LiveRefreshService } from '../../core/services/live-refresh.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { BookingDecisions } from './booking-decisions';
import { TranslationKey } from '../../core/i18n/en';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';

/**
 * The tabs, as the server's tab names and the keys that word them.
 *
 * The name is what travels in `?tab=` and to the API; the words are chosen when the tabs render. They
 * were English literals in a field initialiser once, which no language switch could reach.
 */
const TABS: readonly { readonly key: BookingTab; readonly label: TranslationKey }[] = [
  { key: 'all', label: 'common.all' },
  { key: 'pending', label: 'dealerBookings.tabPending' },
  { key: 'upcoming', label: 'dealerBookings.tabUpcoming' },
  { key: 'active', label: 'dealerBookings.tabActive' },
  { key: 'returned', label: 'dealerBookings.tabReturned' },
  { key: 'completed', label: 'dealerBookings.tabCompleted' },
  { key: 'closed', label: 'dealerBookings.tabClosed' },
  // A live dispute is a flag on a booking, and this tab is every booking carrying it.
  { key: 'disputed', label: 'status.disputed' },
];

/**
 * The dealer's bookings (design: Dealer Console, `isList` for bookings).
 *
 * Tabs are the server's: `?tab=` maps to domain statuses in one place and the counts come from the
 * same mapping, so a tab and its count cannot disagree. The design's "Confirmed" has no domain state
 * and is not offered; "Returned" is, because a dealer needs to see cars back and awaiting settlement.
 */
@Component({
  selector: 'kh-dealer-bookings',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dealer-bookings.component.html',
  imports: [RouterLink, IconComponent],
})
export class DealerBookingsComponent {
  protected readonly t = inject(I18nService).t;
  // Server enum names, in the reader's language, with the dealer's own wording for its queue.
  private readonly statusLabel = inject(I18nService).statusLabel;
  private readonly formats = inject(FormatService);

  /**
   * The row's total, at the currency's own scale.
   *
   * The template used to interpolate the two fields raw, which printed a 110.000 JOD booking as
   * "JOD 110" while the customer app showed "JOD 110.000" for the same booking. Money is
   * formatted in one place for exactly this reason.
   */
  protected rowTotal(booking: BookingListItem): string {
    return this.formats.money(booking.totalPrice, booking.currency);
  }
  private readonly service = inject(DealerBookingsService);
  private readonly ui = inject(ConsoleUiService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly decisions = inject(BookingDecisions);
  private readonly live = inject(LiveRefreshService);

  protected readonly list = this.service.list;
  protected readonly counts = this.service.counts;
  /**
   * The last rows that ARRIVED, not the last request's outcome.
   *
   * `loaded()` answers null the moment a resource errors, which is right for a screen somebody is
   * waiting on and wrong for one re-reading itself in the background. A failed poll would have
   * emptied this queue and filled it again a poll later, under a dealer working through it.
   */
  private readonly listPage = this.service.retainedList;
  private readonly tabCounts = this.service.retainedCounts;
  protected readonly tab = this.service.tab;

  /** The tabs in the reader's language. A `computed`, not a field: a field words them only once. */
  protected readonly tabs = computed(() =>
    TABS.map((option) => ({ key: option.key, label: this.t(option.label) })),
  );

  // The dashboard links here with ?tab=pending; the URL is the source of truth for the tab.
  private readonly tabFromUrl = toSignal(
    this.route.queryParamMap.pipe(map((params) => params.get('tab'))),
    { initialValue: this.route.snapshot.queryParamMap.get('tab') },
  );

  constructor() {
    effect(() => {
      const wanted = this.tabFromUrl();
      const known = TABS.find((option) => option.key === wanted);
      this.service.tab.set(known ? known.key : 'all');
      this.service.page.set(1);
    });

    // This screen is the only one that needs the queue fresh, and the only one that draws all eight
    // counts — so it is the only one that may pay for them. Nothing polls while somebody is looking
    // at the fleet or the reports.
    this.service.watchingTheQueue.set(true);
    inject(DestroyRef).onDestroy(() => this.service.watchingTheQueue.set(false));
  }

  /**
   * When the rows on screen were last confirmed by the server, or null while they are current.
   *
   * Set after two failed quiet refreshes running. One is a hiccup worth nobody's attention; two
   * means the screen should stop implying it is up to date, without throwing away the work the
   * dealer can still see.
   */
  protected readonly staleSince = computed(
    () => this.live.staleSince().get('dealer.bookings') ?? null,
  );

  protected readonly rows = computed(() => this.listPage()?.items ?? []);
  protected readonly total = computed(() => this.listPage()?.totalCount ?? 0);
  protected readonly totalPages = computed(() => this.listPage()?.totalPages ?? 1);

  /**
   * "1 bookings" is the sort of thing that makes a screen look generated. The total picks the noun's
   * form, and Arabic has six of them.
   */
  protected readonly summary = computed(() =>
    this.t('dealerBookings.pageSummary', { shown: this.rows().length, count: this.total() }),
  );
  protected readonly page = this.service.page;

  protected readonly failure = computed(() => {
    const error = this.list.error() as { status?: number; error?: { code?: string } } | undefined;
    if (!error) return null;
    if (error.error?.code === 'dealer.not_registered')
      return this.t('employeeDash.thisAccountIsNot');
    return this.t('dealerBookings.yourBookingsCouldNot');
  });

  protected count(tab: BookingTab): number | null {
    return this.tabCounts()?.[tab] ?? null;
  }

  protected select(tab: BookingTab): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { tab },
      queryParamsHandling: 'merge',
    });
  }

  protected goTo(page: number): void {
    if (page >= 1 && page <= this.totalPages()) this.service.page.set(page);
  }

  /** Loud: somebody asked, or a decision just landed. Exempt from the floor. */
  protected reload(): void {
    this.service.refresh();
    this.live.touch('dealer.counts');
  }

  protected open(booking: BookingListItem): void {
    void this.router.navigate(['/dealer/bookings', booking.bookingId]);
  }

  protected approve(event: Event, booking: BookingListItem): void {
    event.stopPropagation();
    this.decisions.approve(booking.bookingId, booking.reference, this.customer(booking), () =>
      this.reload(),
    );
  }

  protected reject(event: Event, booking: BookingListItem): void {
    event.stopPropagation();
    this.decisions.reject(booking.bookingId, booking.reference, () => this.reload());
  }

  protected tone(booking: BookingListItem): Tone {
    if (booking.hasLiveDispute) return 'bad';
    switch (booking.status) {
      // Waiting on somebody: the dealer's answer, or the customer's deposit.
      case 'Requested':
      case 'Approved':
        return 'warn';
      case 'Confirmed':
        return 'accent';
      case 'PickedUp':
      case 'Returned':
        return 'ok';
      default:
        return 'dim';
    }
  }

  /**
   * The dealer's word for the state, not the domain's identifier.
   *
   * A live dispute is a flag on the booking rather than a status, so it has its own key. Everything
   * else is the dealer-scoped label, which is where `Requested` becomes "Pending" and `PickedUp`
   * becomes "Active".
   */
  protected label(booking: BookingListItem): string {
    if (booking.hasLiveDispute) return this.t('status.disputed');
    return this.statusLabel(booking.status, 'dealerBooking');
  }

  /** "06 Sept → 09 Sept", as one message so Arabic can point its own arrow. */
  protected period(booking: BookingListItem): string {
    return this.t('dealerBookings.periodRange', {
      start: this.formats.dayMonth(booking.periodStart),
      end: this.formats.dayMonth(booking.periodEnd),
    });
  }

  /**
   * The booking's own billed days, as the server froze them.
   *
   * This used to subtract the two instants and round. That answered a different question --
   * elapsed time -- and since the owner settled calendar-day billing on 2026-09-07 it gives a
   * different number: a car out Monday 09:00 and back Thursday 21:00 is three days on the invoice
   * and four to a subtraction. A screen must never be a second source for a figure the server
   * already holds.
   */
  protected days(booking: BookingListItem): string {
    return this.t('booking.days', { count: booking.days });
  }

  protected created(booking: BookingListItem): string {
    return this.formats.dayMonthTime(booking.createdAt);
  }

  protected car(booking: BookingListItem): string {
    return booking.vehicle
      ? `${booking.vehicle.make} ${booking.vehicle.model} ${booking.vehicle.year}`
      : this.t('dealerBookings.vehicleNoLongerListed');
  }

  /**
   * Who booked, as this gallery may name them.
   *
   * A closed customer account keeps its bookings. `customerName` then carries an English sentinel for
   * older clients; the flag is what says so, and the words are the reader's.
   */
  protected customer(booking: BookingListItem): string {
    return booking.customerAccountClosed
      ? this.t('common.customerAccountClosed')
      : booking.customerName;
  }

  /** No initials for a closed account: two letters would stand for a person who is no longer there. */
  protected customerInitials(booking: BookingListItem): string {
    return booking.customerAccountClosed ? '' : this.initials(booking.customerName);
  }

  private initials(name: string): string {
    return name
      .split(' ')
      .slice(0, 2)
      .map((part) => part[0] ?? '')
      .join('');
  }

  protected showToast(title: string, body: string): void {
    this.ui.showToast(title, body);
  }
}
