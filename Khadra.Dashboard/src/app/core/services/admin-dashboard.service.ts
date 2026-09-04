import { httpResource } from '@angular/common/http';
import { Injectable, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter, map, startWith } from 'rxjs';
import {
  ActivityFeed,
  AdminWorkload,
  AttentionQueue,
  BookingCounts,
  BookingTrend,
  CustomerCounts,
  DealerCounts,
  DisputeCounts,
} from '../models/dashboard.api';
import { SessionService } from './session.service';

/**
 * The admin dashboard, one resource per panel.
 *
 * Every URL is relative on purpose. Calls go through the BFF, which holds the API token server-side
 * and proxies `/api/**`; the browser never sees a bearer token and no origin is hardcoded anywhere in
 * the console.
 *
 * This was one `dashboard` resource returning the whole landing screen. Splitting it fixed three
 * things at once:
 *
 *  - The rail read two numbers from it on EVERY screen, so opening the dealer queue fetched the work
 *    queue, the fourteen-day trend and the audit feed to render two integers.
 *  - Being one root resource with a constant URL, it fired once when the shell first injected this
 *    service and never again. The badges were frozen at the first paint of the session: approving a
 *    dealer left the rail still asking for it, until a full page reload.
 *  - One failing reader blanked the entire screen. Now each panel fails, retries and reloads alone.
 */
@Injectable({ providedIn: 'root' })
export class AdminDashboardService {
  private readonly session = inject(SessionService);

  /**
   * Only an administrator may ask these questions.
   *
   * Not a nicety: this service is injected by the SIDEBAR, which every signed-in user sees, and an
   * `httpResource` fires as soon as it is created. A dealer session was therefore firing
   * `/api/v1/admin/dashboard` on every shell load and swallowing a 403 — the interceptor only acts on
   * 401, so it failed silently and nobody noticed. Returning `undefined` from the URL factory leaves
   * the resource idle instead of asking a question the caller has no business asking.
   */
  private readonly isAdmin = computed(() => this.session.user()?.role === 'Admin');

  /**
   * Bumped on every completed navigation.
   *
   * The workload figures are what the rail badges, and a badge is only worth having if it is true
   * now. Re-reading them per screen is affordable precisely because this endpoint is two counts;
   * the composite it replaced never could have been asked at this cadence.
   */
  private readonly navigation = toSignal(
    inject(Router).events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map((_, index) => index + 1),
      startWith(0),
    ),
    { initialValue: 0 },
  );

  readonly workload = httpResource<AdminWorkload>(() => {
    // Read so the resource re-runs when it changes; the value itself is not part of the request.
    this.navigation();
    return this.isAdmin() ? '/api/v1/admin/workload' : undefined;
  });

  private adminUrl(path: string): string | undefined {
    return this.isAdmin() ? `/api/v1/admin/dashboard/${path}` : undefined;
  }

  readonly dealerCounts = httpResource<DealerCounts>(() => this.adminUrl('dealer-counts'));
  readonly bookingCounts = httpResource<BookingCounts>(() => this.adminUrl('booking-counts'));
  readonly customerCounts = httpResource<CustomerCounts>(() => this.adminUrl('customer-counts'));
  readonly disputeCounts = httpResource<DisputeCounts>(() => this.adminUrl('dispute-counts'));
  readonly attentionQueue = httpResource<AttentionQueue>(() => this.adminUrl('attention-queue'));
  readonly bookingTrend = httpResource<BookingTrend>(() => this.adminUrl('booking-trend'));
  readonly activity = httpResource<ActivityFeed>(() => this.adminUrl('activity'));

  /** Everything the dashboard screen draws. The rail's workload refreshes on its own cadence. */
  reload(): void {
    this.dealerCounts.reload();
    this.bookingCounts.reload();
    this.customerCounts.reload();
    this.disputeCounts.reload();
    this.attentionQueue.reload();
    this.bookingTrend.reload();
    this.activity.reload();
  }

  /**
   * Called after any decision that changes what the platform owes: approving a dealer, resolving a
   * dispute. Without this the rail goes on advertising work that is already done.
   */
  refreshWorkload(): void {
    this.workload.reload();
  }
}
