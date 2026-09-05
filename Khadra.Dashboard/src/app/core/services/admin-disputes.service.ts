import { HttpClient, httpResource } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PagedResult } from '../models/bookings.api';
import { Dispute, DisputeListItem, DisputeQueueCounts } from '../models/disputes.api';
import { AdminDashboardService } from './admin-dashboard.service';

/** What the queue is filtered to. `live` is the default because it is the queue an admin works. */
export type DisputeQueue = 'live' | 'Open' | 'UnderReview' | 'Resolved' | 'Withdrawn';

/**
 * The Admin's dispute queue and workspace (spec 3.3).
 *
 * The one place on the platform where a decision about money is made, so nothing here is computed
 * in the browser: the split the admin types is sent as three legs and the server checks it balances
 * against the deposit the booking actually froze.
 */
@Injectable({ providedIn: 'root' })
export class AdminDisputesService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/admin/disputes';
  private readonly dashboard = inject(AdminDashboardService);

  readonly queue = signal<DisputeQueue>('live');
  readonly overdueOnly = signal(false);
  readonly page = signal(1);

  /** The filters both the page and its counts are read under, so the two cannot describe different sets. */
  private readonly filter = computed(() => ({
    // The API reads a missing status as "live"; sending the word would be a status that does not exist.
    ...(this.queue() === 'live' ? {} : { status: this.queue() }),
    overdueOnly: this.overdueOnly(),
  }));

  readonly list = httpResource<PagedResult<DisputeListItem>>(() => ({
    url: this.base,
    params: { ...this.filter(), page: this.page(), pageSize: 25 },
  }));

  /**
   * How the queue is shaped, for ALL of it under these filters.
   *
   * The screen used to count overdue and unassigned from the rows it happened to be holding and
   * print them beside a platform total. With ten tickets and a page size of twenty-five that was
   * right by accident; at thirty it reports a page's figures as though they were the queue's.
   */
  readonly counts = httpResource<DisputeQueueCounts>(() => ({
    url: `${this.base}/counts`,
    params: this.filter(),
  }));

  readonly viewing = signal<string | null>(null);

  readonly dispute = httpResource<Dispute>(() => {
    const id = this.viewing();
    return id ? `${this.base}/${id}` : undefined;
  });

  /** Takes the ticket on. Recorded, but not a gate: any admin may still resolve it. */
  assign(ticketId: string): Promise<Dispute> {
    return firstValueFrom(this.http.post<Dispute>(`${this.base}/${ticketId}/assign`, {}));
  }

  /**
   * The decision: a three-way split of the held deposit plus an optional dealer charge. The legs
   * must add up to exactly what the booking holds, and the server is the judge of that.
   */
  resolve(
    ticketId: string,
    split: {
      readonly refundToCustomer: number;
      readonly retainedByPlatform: number;
      readonly transferredToDealer: number;
      readonly dealerCharge: number | null;
      readonly note: string;
    },
  ): Promise<Dispute> {
    return firstValueFrom(this.http.post<Dispute>(`${this.base}/${ticketId}/resolve`, split));
  }

  refreshList(): void {
    this.list.reload();
    this.counts.reload();
    this.dashboard.refreshWorkload();
  }

  /**
   * After taking or resolving a ticket, the rail's live-dispute count is stale too.
   *
   * It used to come from a whole-dashboard snapshot fetched once per page load, so an admin who
   * resolved every open ticket still saw the badge claiming they were all waiting.
   */
  refresh(): void {
    this.dispute.reload();
    this.dashboard.refreshWorkload();
  }
}
