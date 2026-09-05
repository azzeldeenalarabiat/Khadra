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

  /** After a decision: the row, the counts and the open detail all describe the same booking. */
  refresh(): void {
    this.list.reload();
    this.counts.reload();
    this.booking.reload();
    // The dashboard tiles and the activity trail are read from bookings too, and neither belongs to
    // this service; without this the dealer's own decision is missing from both until a page reload.
    this.console.refreshDerived();
  }
}
