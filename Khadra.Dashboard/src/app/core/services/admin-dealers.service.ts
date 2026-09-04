import { HttpClient, httpResource } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { DealerListItem, DealerProfile, DealerReview, PagedResult } from '../models/dealers.api';
import { AdminDashboardService } from './admin-dashboard.service';

/**
 * The Admin's dealer queue.
 *
 * Reads are resources so the screen gets loading and error states for free; the five decisions are
 * plain calls, because a decision is something the admin does once and then wants the list to
 * reflect, not something to re-run on a signal change.
 */
@Injectable({ providedIn: 'root' })
export class AdminDealersService {
  private readonly http = inject(HttpClient);
  private readonly dashboard = inject(AdminDashboardService);

  /** Filter state the list resource reacts to. */
  readonly status = signal<string | null>(null);
  readonly search = signal<string>('');
  readonly page = signal<number>(1);

  readonly dealers = httpResource<PagedResult<DealerListItem>>(() => {
    const params: Record<string, string | number> = { page: this.page(), pageSize: 20 };
    const status = this.status();
    if (status) params['status'] = status;
    const search = this.search().trim();
    if (search) params['search'] = search;
    return { url: '/api/v1/admin/dealers', params };
  });

  /** The dealer currently open for review, or null when the screen is the list. */
  readonly reviewing = signal<string | null>(null);

  readonly review = httpResource<DealerReview>(() => {
    const id = this.reviewing();
    // Returning undefined leaves the resource idle rather than firing a request for nothing.
    return id ? { url: `/api/v1/admin/dealers/${id}` } : undefined;
  });

  private async decide(dealerId: string, action: string, reason?: string): Promise<DealerProfile> {
    const token = await firstValueFrom(this.http.get<{ requestToken: string }>('/bff/antiforgery'));
    return firstValueFrom(
      this.http.post<DealerProfile>(
        `/api/v1/admin/dealers/${dealerId}/${action}`,
        reason === undefined ? {} : { reason },
        { headers: { 'X-XSRF-TOKEN': token.requestToken } },
      ),
    );
  }

  approve = (dealerId: string) => this.decide(dealerId, 'approve');

  reject = (dealerId: string, reason: string) => this.decide(dealerId, 'reject', reason);

  requestClarification = (dealerId: string, reason: string) =>
    this.decide(dealerId, 'request-clarification', reason);

  suspend = (dealerId: string, reason: string) => this.decide(dealerId, 'suspend', reason);

  reactivate = (dealerId: string) => this.decide(dealerId, 'reactivate');

  /**
   * After a decision three things are stale: the application being read, the queue it came from, and
   * the rail's count of what the platform still owes.
   *
   * The rail was the one nobody refreshed. It read a whole-dashboard snapshot fetched once when the
   * console loaded, so approving a dealer left the badge advertising that same application until the
   * admin reloaded the page — the count was live-looking rather than live.
   */
  refresh(): void {
    this.review.reload();
    this.dealers.reload();
    this.dashboard.refreshWorkload();
  }
}
