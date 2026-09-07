import { HttpClient, httpResource } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PagedResult } from '../models/bookings.api';
import { CustomerCounts, CustomerListItem, CustomerProfile } from '../models/customers.api';
import { AdminDashboardService } from './admin-dashboard.service';

/**
 * The people who rent (spec 5), through `/api/v1/admin/customers`.
 *
 * Every route there speaks about customers only: a user who is not one answers 404, so this service
 * cannot be pointed at a dealer or another administrator by changing an id in the URL.
 */
@Injectable({ providedIn: 'root' })
export class AdminCustomersService {
  private readonly http = inject(HttpClient);
  private readonly dashboard = inject(AdminDashboardService);
  private readonly base = '/api/v1/admin/customers';

  readonly status = signal<string | null>(null);
  readonly unverifiedOnly = signal(false);
  readonly search = signal('');
  readonly page = signal(1);
  readonly pageSize = 20;

  readonly list = httpResource<PagedResult<CustomerListItem>>(() => {
    const params: Record<string, string | number | boolean> = {
      page: this.page(),
      pageSize: this.pageSize,
    };
    const status = this.status();
    if (status) params['status'] = status;
    if (this.unverifiedOnly()) params['unverifiedOnly'] = true;
    const search = this.search().trim();
    if (search) params['search'] = search;
    return { url: this.base, params };
  });

  /** The counts behind the filters, so each chip carries the database's answer. */
  readonly counts = httpResource<CustomerCounts>(() => `${this.base}/counts`);

  readonly viewing = signal<string | null>(null);

  readonly customer = httpResource<CustomerProfile>(() => {
    const id = this.viewing();
    return id ? `${this.base}/${id}` : undefined;
  });

  private async act(userId: string, action: string, body: unknown = {}): Promise<CustomerProfile> {
    const token = await firstValueFrom(this.http.get<{ requestToken: string }>('/bff/antiforgery'));
    return firstValueFrom(
      this.http.post<CustomerProfile>(`${this.base}/${userId}/${action}`, body, {
        headers: { 'X-XSRF-TOKEN': token.requestToken },
      }),
    );
  }

  suspend = (userId: string, reason: string) => this.act(userId, 'suspend', { reason });

  reactivate = (userId: string) => this.act(userId, 'reactivate');

  refresh(): void {
    this.customer.reload();
    this.list.reload();
    this.counts.reload();
    this.dashboard.refreshWorkload();
  }
}
