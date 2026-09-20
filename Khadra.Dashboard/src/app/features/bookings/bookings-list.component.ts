import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { BookingListItem, BookingStatus } from '../../core/models/bookings.api';
import { Tone } from '../../core/models/console.models';
import { AdminBookingsService } from '../../core/services/admin-bookings.service';
import { BookingTab } from '../../core/services/dealer-bookings.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { TranslationKey } from '../../core/i18n/en';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { Language } from '../../core/i18n/language';
import { ProblemSnapshot, serverSentence, snapshotProblem } from '../../core/i18n/problem';

/** The dealer's tabs, plus the one status the platform needs that they cannot express. */
type AdminBookingTab = BookingTab | 'unpaid';

/**
 * The tabs, as the names that travel in `?tab=` and the keys that word them.
 *
 * The words are chosen when the tabs render. They were English literals in a field initialiser once,
 * which no language switch could reach. The shared tab names read the same here as on the dealer's
 * own list because they are the same server vocabulary.
 */
const TABS: readonly { readonly key: AdminBookingTab; readonly label: TranslationKey }[] = [
  { key: 'all', label: 'common.all' },
  { key: 'unpaid', label: 'bookingsList.tabUnpaid' },
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
 * Every booking on the platform (spec 3.3).
 *
 * The tabs are the server's vocabulary and so are their counts, resolved from one mapping, so a tab
 * and the number beside it cannot disagree. Unlike the dealer's own list this one includes bookings
 * whose deposit never cleared: they are holding a car, which is the state an administrator most
 * needs to see and the one a dealership is deliberately not shown.
 */
@Component({
  selector: 'kh-bookings-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './bookings-list.component.html',
  imports: [RouterLink, IconComponent],
})
export class BookingsListComponent {
  private readonly i18n = inject(I18nService);
  protected readonly t = this.i18n.t;
  private readonly formats = inject(FormatService);
  private readonly service = inject(AdminBookingsService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly resource = this.service.list;
  protected readonly countsResource = this.service.counts;
  protected readonly tab = this.service.tab;
  protected readonly search = this.service.search;

  private readonly loadedPage = loaded(this.resource);
  private readonly loadedCounts = loaded(this.countsResource);

  /**
   * 'unpaid' is not one of the server's tabs: it is the Approved status, asked for directly. Since
   * 2026-09-07 that is exactly the booking a dealer has agreed to and nobody has paid for, which is
   * the platform's own concern rather than a queue the dealer console needs.
   *
   * A `computed`, not a field: a field words the tabs once, in whichever language was on screen then.
   */
  protected readonly tabs = computed(() =>
    TABS.map((option) => ({ key: option.key, label: this.t(option.label) })),
  );

  /**
   * Filters that arrived in the URL.
   *
   * The customer profile and the dealer page both link here scoped to one party, and the dashboard
   * links here by tab. The URL is the source of truth so the browser's back button and a bookmark
   * both land on the same list.
   */
  private readonly params = toSignal(
    this.route.queryParamMap.pipe(
      map((query) => ({
        tab: query.get('tab'),
        dealerId: query.get('dealerId'),
        customerId: query.get('customerId'),
      })),
    ),
    {
      initialValue: {
        tab: this.route.snapshot.queryParamMap.get('tab'),
        dealerId: this.route.snapshot.queryParamMap.get('dealerId'),
        customerId: this.route.snapshot.queryParamMap.get('customerId'),
      },
    },
  );

  constructor() {
    effect(() => {
      const wanted = this.params();
      const known = TABS.find((candidate) => candidate.key === wanted.tab);
      const key: AdminBookingTab = known ? known.key : 'all';
      this.service.tab.set(key === 'unpaid' ? 'all' : key);
      this.service.status.set(key === 'unpaid' ? 'Approved' : null);
      this.service.dealerId.set(wanted.dealerId);
      this.service.customerId.set(wanted.customerId);
      this.service.page.set(1);
    });
  }

  protected readonly rows = computed(() => this.loadedPage()?.items ?? []);
  protected readonly total = computed(() => this.loadedPage()?.totalCount ?? 0);
  protected readonly totalPages = computed(() => this.loadedPage()?.totalPages ?? 1);
  protected readonly page = computed(() => this.loadedPage()?.page ?? 1);

  /** The count behind a tab, or null until the server has answered. Never a zero it invented. */
  protected count(tab: AdminBookingTab): number | null {
    // No count for Unpaid: the tab-counts endpoint answers for the tab vocabulary, and a number
    // this screen worked out for itself would be a second source for a figure the server owns.
    if (tab === 'unpaid') return null;
    const counts = this.loadedCounts();
    return counts ? (counts[tab] ?? null) : null;
  }

  /**
   * "Showing 21–40 of 57 bookings", as ONE plural message: the total picks the noun's form, and
   * Arabic has six of them.
   */
  protected readonly summary = computed(() => {
    const page = this.loadedPage();
    if (!page) return '';
    const from = page.totalCount === 0 ? 0 : (page.page - 1) * page.pageSize + 1;
    const to = Math.min(page.page * page.pageSize, page.totalCount);
    return this.t('bookingsList.pageSummary', { from, to, count: page.totalCount });
  });

  /** Whether the list is narrowed to one party, so the screen can say so and offer a way out. */
  protected readonly scopedTo = computed(() => {
    const rows = this.rows();
    if (this.service.dealerId() && rows.length)
      return this.t('bookingsList.scopedToDealer', { name: this.dealerName(rows[0]) });
    if (this.service.customerId() && rows.length)
      return this.t('bookingsList.scopedToCustomer', { name: this.customerName(rows[0]) });
    if (this.service.dealerId() || this.service.customerId())
      return this.t('bookingsList.scopedToOneParty');
    return null;
  });

  /** The dealership's name, or the fact that it has left the platform. Never the English stand-in. */
  protected dealerName(row: BookingListItem): string {
    return row.dealerRemoved ? this.t('common.dealerNoLongerOnPlatform') : row.dealerName;
  }

  /** The customer's name, or the fact that the account was closed. Never the English stand-in. */
  protected customerName(row: BookingListItem): string {
    return row.customerAccountClosed ? this.t('common.customerAccountClosed') : row.customerName;
  }

  /** A failed load, held as the resource's facts and worded here, so a language switch re-words it. */
  protected readonly failure = computed(() => {
    const error = this.resource.error();
    if (!error) return null;
    return describe(snapshotProblem(error), this.t, this.i18n.lang());
  });

  protected select(tab: AdminBookingTab): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { tab },
      queryParamsHandling: 'merge',
    });
  }

  protected clearScope(): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { dealerId: null, customerId: null },
      queryParamsHandling: 'merge',
    });
  }

  protected setSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
    this.service.page.set(1);
  }

  protected goTo(page: number): void {
    if (page >= 1 && page <= this.totalPages()) this.service.page.set(page);
  }

  protected reload(): void {
    this.resource.reload();
  }

  /**
   * The colour a status is read in. Money at risk is bad, work outstanding is a caution, a booking
   * running or finished cleanly is neither.
   */
  protected tone(row: BookingListItem): Tone {
    if (row.hasLiveDispute) return 'bad';
    return STATUS_TONES[row.status] ?? 'dim';
  }

  /** The server's status name, in the reader's language and in the booking sense of the word. */
  protected label(status: BookingStatus): string {
    return this.i18n.statusLabel(status, 'booking');
  }

  protected when(iso: string): string {
    return this.formats.date(iso);
  }

  /** The row's total at the currency's own scale, with the code the value carries. */
  protected rowTotal(row: BookingListItem): string {
    return this.formats.money(row.totalPrice, row.currency);
  }

  protected vehicleLabel(row: BookingListItem): string {
    const vehicle = row.vehicle;
    // The booking outlives the listing, so a delisted car has no label to show — and inventing one
    // would put a car on the screen that is no longer on the platform.
    return vehicle
      ? `${vehicle.make} ${vehicle.model} ${vehicle.year}`
      : this.t('bookingsList.vehicleDelisted');
  }
}

const STATUS_TONES: Readonly<Partial<Record<BookingStatus, Tone>>> = {
  Requested: 'warn',
  // Approved is a warning, not an accent: the dealer has said yes and the deposit has not arrived,
  // so the car is held against nothing and a clock is running on it.
  Approved: 'warn',
  Confirmed: 'accent',
  PickedUp: 'accent',
  Returned: 'warn',
  Completed: 'ok',
  Rejected: 'dim',
  Cancelled: 'dim',
  Expired: 'dim',
  NoShow: 'bad',
};

/** Why the list could not load, in the language on screen when it is shown. */
function describe(
  problem: ProblemSnapshot,
  t: (key: TranslationKey) => string,
  language: Language,
): string {
  if (problem.status === 403) return t('bookingsList.thePlatformBookingList');
  return serverSentence(problem, language, t) ?? t('bookingsList.theBookingsCouldNot');
}
