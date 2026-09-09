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
import { I18nService } from '../../core/i18n/i18n.service';

/** The dealer's tabs, plus the one status the platform needs that they cannot express. */
type AdminBookingTab = BookingTab | 'unpaid';

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
  protected readonly t = inject(I18nService).t;
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
   */
  protected readonly tabs: readonly { readonly key: AdminBookingTab; readonly label: string }[] = [
    { key: 'all', label: 'All' },
    { key: 'unpaid', label: 'Unpaid' },
    { key: 'pending', label: 'Pending' },
    { key: 'upcoming', label: 'Upcoming' },
    { key: 'active', label: 'Active' },
    { key: 'returned', label: 'Returned' },
    { key: 'completed', label: 'Completed' },
    { key: 'closed', label: 'Closed' },
    { key: 'disputed', label: 'Disputed' },
  ];

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
      const known = this.tabs.find((candidate) => candidate.key === wanted.tab);
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

  protected readonly summary = computed(() => {
    const page = this.loadedPage();
    if (!page) return '';
    const from = page.totalCount === 0 ? 0 : (page.page - 1) * page.pageSize + 1;
    const to = Math.min(page.page * page.pageSize, page.totalCount);
    const noun = page.totalCount === 1 ? 'booking' : 'bookings';
    return `Showing ${from}–${to} of ${page.totalCount} ${noun}`;
  });

  /** Whether the list is narrowed to one party, so the screen can say so and offer a way out. */
  protected readonly scopedTo = computed(() => {
    const rows = this.rows();
    if (this.service.dealerId() && rows.length) return `dealer: ${rows[0].dealerName}`;
    if (this.service.customerId() && rows.length) return `customer: ${rows[0].customerName}`;
    if (this.service.dealerId() || this.service.customerId()) return this.t('bookingsList.oneParty');
    return null;
  });

  protected readonly failure = computed(() => {
    const error = this.resource.error() as { status?: number } | undefined;
    if (!error) return null;
    if (error.status === 403) return this.t('bookingsList.thePlatformBookingList');
    return this.t('bookingsList.theBookingsCouldNot');
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

  protected label(status: BookingStatus): string {
    return status.replace(/([a-z])([A-Z])/g, '$1 $2');
  }

  protected when(iso: string): string {
    return new Date(iso).toLocaleDateString('en-GB', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
    });
  }

  protected vehicleLabel(row: BookingListItem): string {
    const vehicle = row.vehicle;
    // The booking outlives the listing, so a delisted car has no label to show — and inventing one
    // would put a car on the screen that is no longer on the platform.
    return vehicle ? `${vehicle.make} ${vehicle.model} ${vehicle.year}` : this.t('bookingsList.vehicleDelisted');
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
