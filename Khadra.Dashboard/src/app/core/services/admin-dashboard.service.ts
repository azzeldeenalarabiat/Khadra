import { httpResource } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { AdminDashboard } from '../models/dashboard.api';

/**
 * The admin dashboard snapshot.
 *
 * The URL is relative on purpose. Every call goes through the BFF, which holds the API token
 * server-side and proxies `/api/**`; the browser never sees a bearer token and no origin is hardcoded
 * anywhere in the console.
 */
@Injectable({ providedIn: 'root' })
export class AdminDashboardService {
  readonly dashboard = httpResource<AdminDashboard>(() => '/api/v1/admin/dashboard');

  reload(): void {
    this.dashboard.reload();
  }
}
