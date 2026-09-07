import { HttpClient, httpResource } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Booking, BookingListItem, PagedResult } from '../models/bookings.api';
import { BookingTab, TabCounts } from './dealer-bookings.service';
import { AdminDashboardService } from './admin-dashboard.service';

/**
 * Every booking on the platform (spec 3.3), through `/api/v1/admin/bookings`.
 *
 * The same tab vocabulary the dealer's own list uses, resolved server-side to domain statuses, so
 * the console never encodes state names and a tab and its count cannot disagree. What differs is the
 * scope: this asks for the whole platform, and the API includes the unpaid requests a dealer is not
 * shown — they are holding cars, which is exactly what an administrator is looking for.
 */
@Injectable({ providedIn: 'root' })
export class AdminBookingsService {
  private readonly http = inject(HttpClient);
  private readonly dashboard = inject(AdminDashboardService);
  private readonly base = '/api/v1/admin/bookings';

  readonly tab = signal<BookingTab>('all');

  /**
   * A status the tabs cannot reach.
   *
   * `BookingTabs` is the DEALER's vocabulary, and to a dealership an approved booking is simply one
   * of the week's bookings whether or not the deposit has landed yet. To the platform the unpaid
   * ones are cars being held against nothing, so the Admin's "Unpaid" tab asks for that status
   * directly rather than adding a tab to a mapping the dealer console shares.
   */
  readonly status = signal<string | null>(null);
  readonly search = signal('');
  readonly dealerId = signal<string | null>(null);
  readonly customerId = signal<string | null>(null);
  readonly page = signal(1);
  readonly pageSize = 20;

  private readonly scope = computed(() => {
    const params: Record<string, string | number> = {};
    const dealer = this.dealerId();
    if (dealer) params['dealerId'] = dealer;
    const customer = this.customerId();
    if (customer) params['customerId'] = customer;
    return params;
  });

  readonly list = httpResource<PagedResult<BookingListItem>>(() => {
    const params: Record<string, string | number> = {
      ...this.scope(),
      page: this.page(),
      pageSize: this.pageSize,
    };
    const status = this.status();
    if (status) params['status'] = status;
    else params['tab'] = this.tab();
    // A reference is quoted whole, so the API matches it exactly rather than as a prefix.
    const reference = this.search().trim();
    if (reference) params['reference'] = reference;
    return { url: this.base, params };
  });

  readonly counts = httpResource<TabCounts>(() => ({
    url: `${this.base}/tab-counts`,
    params: this.scope(),
  }));

  /** The booking a detail screen is showing; null keeps the resource idle. */
  readonly viewing = signal<string | null>(null);

  readonly booking = httpResource<Booking>(() => {
    const id = this.viewing();
    return id ? `${this.base}/${id}` : undefined;
  });

  private async act(bookingId: string, action: string, body: unknown = {}): Promise<Booking> {
    const token = await firstValueFrom(this.http.get<{ requestToken: string }>('/bff/antiforgery'));
    return firstValueFrom(
      this.http.post<Booking>(`${this.base}/${bookingId}/${action}`, body, {
        headers: { 'X-XSRF-TOKEN': token.requestToken },
      }),
    );
  }

  /** Cancels on the platform's behalf. Assesses no penalty against either party. */
  cancel = (bookingId: string, reason: string) => this.act(bookingId, 'cancel', { reason });

  /** Ends a booking whose own frozen window has run out. The server refuses it early. */
  expire = (bookingId: string) => this.act(bookingId, 'expire');

  markNoShow = (bookingId: string) => this.act(bookingId, 'no-show');

  /**
   * After an intervention the list, the tab counts and the rail's own figures are all stale — a
   * cancelled booking leaves the tab it was in and may close a dispute's booking behind it.
   */
  refresh(): void {
    this.booking.reload();
    this.list.reload();
    this.counts.reload();
    this.dashboard.refreshWorkload();
  }
}
