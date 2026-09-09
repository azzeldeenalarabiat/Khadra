import { HttpClient, httpResource } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Booking, BookingListItem, PagedResult } from '../models/bookings.api';
import { DealerConsoleService } from './dealer-console.service';

/**
 * The dealer's bookings tab, as the API sees it (Khadra.Application/Bookings/ReadBookings).
 *
 * Tabs are resolved server-side to domain statuses, so the console never encodes state names and the
 * tab counts are the database's answer rather than a client-side guess over one page.
 */
export type BookingTab =
  'all' | 'pending' | 'upcoming' | 'active' | 'returned' | 'completed' | 'closed' | 'disputed';

export type TabCounts = Readonly<Record<BookingTab, number>>;

/**
 * What the platform will tell a gallery about the customer in front of them.
 *
 * AGGREGATES ONLY, and every field is the server's own count over bookings it closed itself. There
 * is no list of individual ratings and there never will be: a rating dated last Tuesday would tell
 * this gallery when the customer rented from a competitor.
 *
 * Read through the BOOKING, never by customer id. The endpoint has no customer-id form, so a dealer
 * session cannot be turned into a lookup service over the customer base.
 */
export interface CustomerReputation {
  readonly dealerRating: { readonly average: number | null; readonly count: number };
  readonly completedRentals: number;
  readonly completedRentalsWithThisDealer: number;
  readonly noShows: number;
  readonly lateCancellations: number;
  readonly disputesResolvedAgainstCustomer: number;
  readonly customerSince: string;
  /** False when the platform has nothing to say, so the panel can say THAT rather than show zeros. */
  readonly hasHistory: boolean;
}

/** The gallery's own rating of a customer. A score and nothing else -- there is no comment field. */
export interface CustomerRating {
  readonly reviewId: string;
  readonly bookingId: string;
  readonly rating: number;
  readonly createdAt: string;
}

export interface HandoverInput {
  readonly odometerKm: number | null;
  readonly fuelLevel: number | null;
  readonly notes: string | null;
  readonly cashCollected: number | null;
}

/** All server calls go through the BFF; the browser never holds an API token. */
@Injectable({ providedIn: 'root' })
export class DealerBookingsService {
  private readonly http = inject(HttpClient);
  private readonly console = inject(DealerConsoleService);
  private readonly base = '/api/v1/bookings';

  readonly tab = signal<BookingTab>('all');
  readonly page = signal(1);
  readonly pageSize = 20;

  readonly list = httpResource<PagedResult<BookingListItem>>(() => ({
    url: this.base,
    params: { tab: this.tab(), page: this.page(), pageSize: this.pageSize },
  }));

  readonly counts = httpResource<TabCounts>(() => `${this.base}/tab-counts`);

  /** The booking a detail screen is showing; null keeps the resource idle. */
  readonly viewing = signal<string | null>(null);

  readonly booking = httpResource<Booking>(() => {
    const id = this.viewing();
    return id ? `${this.base}/${id}` : undefined;
  });

  approve(bookingId: string, note: string | null): Promise<Booking> {
    return firstValueFrom(this.http.post<Booking>(`${this.base}/${bookingId}/approve`, { note }));
  }

  reject(bookingId: string, reasonCode: string, details: string): Promise<Booking> {
    return firstValueFrom(
      this.http.post<Booking>(`${this.base}/${bookingId}/reject`, { reasonCode, details }),
    );
  }

  recordPickup(bookingId: string, handover: HandoverInput): Promise<Booking> {
    return firstValueFrom(this.http.post<Booking>(`${this.base}/${bookingId}/pickup`, handover));
  }

  recordReturn(bookingId: string, handover: HandoverInput): Promise<Booking> {
    return firstValueFrom(this.http.post<Booking>(`${this.base}/${bookingId}/return`, handover));
  }

  /**
   * What the platform knows about the customer on the booking being viewed.
   *
   * Answers 409 once the booking is no longer live: a gallery may read this while they are deciding
   * about, or holding, a booking with that person, and no longer. The panel treats that as "nothing
   * to show" rather than an error, because it is not one -- it is the access rule working.
   */
  readonly reputation = httpResource<CustomerReputation>(() => {
    const id = this.viewing();
    return id ? `${this.base}/${id}/customer-reputation` : undefined;
  });

  /** The gallery's own rating of this booking's customer, or null if they have not left one. */
  readonly customerRating = httpResource<CustomerRating | null>(() => {
    const id = this.viewing();
    return id ? `${this.base}/${id}/customer-rating` : undefined;
  });

  rateCustomer(bookingId: string, rating: number): Promise<CustomerRating> {
    return firstValueFrom(
      this.http.post<CustomerRating>(`${this.base}/${bookingId}/customer-rating`, { rating }),
    );
  }

  /** After a decision: the row, the counts and the open detail all describe the same booking. */
  refresh(): void {
    this.list.reload();
    this.counts.reload();
    this.booking.reload();
    this.reputation.reload();
    this.customerRating.reload();
    // The dashboard tiles and the activity trail are read from bookings too, and neither belongs to
    // this service; without this the dealer's own decision is missing from both until a page reload.
    this.console.refreshDerived();
  }
}
