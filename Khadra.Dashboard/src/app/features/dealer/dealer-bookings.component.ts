import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { Tone } from '../../core/models/console.models';
import { BookingListItem } from '../../core/models/bookings.api';
import { BookingTab, DealerBookingsService } from '../../core/services/dealer-bookings.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { BookingDecisions } from './booking-decisions';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';

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

  protected readonly list = this.service.list;
  protected readonly counts = this.service.counts;
  /** Guarded: `value()` throws in the error state. */
  private readonly listPage = loaded(this.list);
  private readonly tabCounts = loaded(this.counts);
  protected readonly tab = this.service.tab;

  protected readonly tabs: readonly { readonly key: BookingTab; readonly label: string }[] = [
    { key: 'all', label: 'All' },
    { key: 'pending', label: 'Pending' },
    { key: 'upcoming', label: 'Upcoming' },
    { key: 'active', label: 'Active' },
    { key: 'returned', label: 'Returned' },
    { key: 'completed', label: 'Completed' },
    { key: 'closed', label: 'Closed' },
    { key: 'disputed', label: 'Disputed' },
  ];

  // The dashboard links here with ?tab=pending; the URL is the source of truth for the tab.
  private readonly tabFromUrl = toSignal(
    this.route.queryParamMap.pipe(map((params) => params.get('tab'))),
    { initialValue: this.route.snapshot.queryParamMap.get('tab') },
  );

  constructor() {
    effect(() => {
      const wanted = this.tabFromUrl();
      const known = this.tabs.find((t) => t.key === wanted);
      this.service.tab.set(known ? known.key : 'all');
      this.service.page.set(1);
    });
  }

  protected readonly rows = computed(() => this.listPage()?.items ?? []);
  protected readonly total = computed(() => this.listPage()?.totalCount ?? 0);
  protected readonly totalPages = computed(() => this.listPage()?.totalPages ?? 1);

  /** "1 bookings" is the sort of thing that makes a screen look generated. */
  protected readonly summary = computed(() => {
    const total = this.total();
    return `${this.rows().length} of ${total} ${total === 1 ? 'booking' : 'bookings'}`;
  });
  protected readonly page = this.service.page;

  protected readonly failure = computed(() => {
    const error = this.list.error() as { status?: number; error?: { code?: string } } | undefined;
    if (!error) return null;
    if (error.error?.code === 'dealer.not_registered')
      return 'This account is not part of a dealership.';
    return 'Your bookings could not be loaded. Nothing has been changed.';
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

  protected reload(): void {
    this.list.reload();
    this.counts.reload();
  }

  protected open(booking: BookingListItem): void {
    void this.router.navigate(['/dealer/bookings', booking.bookingId]);
  }

  protected approve(event: Event, booking: BookingListItem): void {
    event.stopPropagation();
    this.decisions.approve(booking.bookingId, booking.reference, booking.customerName, () =>
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

  /** The dealer's word for the state, not the domain's identifier. */
  protected label(booking: BookingListItem): string {
    if (booking.hasLiveDispute) return 'Disputed';
    switch (booking.status) {
      case 'Requested':
        return 'Pending';
      case 'Approved':
        return 'Awaiting deposit';
      case 'PickedUp':
        return 'Active';
      case 'NoShow':
        return 'No-show';
      default:
        return booking.status;
    }
  }

  protected period(booking: BookingListItem): string {
    const f = (iso: string) =>
      new Date(iso).toLocaleDateString('en-GB', { day: '2-digit', month: 'short' });
    return `${f(booking.periodStart)} → ${f(booking.periodEnd)}`;
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
    return new Date(booking.createdAt).toLocaleString('en-GB', {
      day: '2-digit',
      month: 'short',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  protected car(booking: BookingListItem): string {
    return booking.vehicle
      ? `${booking.vehicle.make} ${booking.vehicle.model} ${booking.vehicle.year}`
      : 'Vehicle no longer listed';
  }

  protected initials(name: string): string {
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
