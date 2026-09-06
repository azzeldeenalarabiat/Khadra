import { HttpClient, httpResource } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PagedResult } from '../models/bookings.api';
import {
  BrandingUpload,
  DealerActivityEntry,
  DealerDashboard,
  DealerReport,
  DeliverySettingsView,
  Employee,
  InviteEmployeeRequest,
  ReportPeriod,
  UpdateProfileRequest,
} from '../models/dealer-console.api';
import { DealerProfile } from '../models/dealers.api';
import { SessionService } from './session.service';

/**
 * The dealer console's data (design: Dealer Console.dc.html).
 *
 * One service for the dealership itself: who it is, its dashboard, its reports, its staff, its page.
 * Bookings and the fleet have their own services. Every call goes through the BFF; the browser never
 * holds an API token.
 */
@Injectable({ providedIn: 'root' })
export class DealerConsoleService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/dealers/me';

  /**
   * Only dealer staff may ask these questions.
   *
   * Not a nicety, and the same trap `AdminDashboardService` documents from the other direction: an
   * `httpResource` fires the moment it is created, and this service is now injected by the TOPBAR,
   * which every signed-in user sees. Without this gate an administrator opening any screen fired six
   * `/api/v1/dealers/me/*` requests and swallowed six 403s — the interceptor only acts on 401, so
   * they would have failed silently.
   */
  private readonly session = inject(SessionService);
  private readonly isDealer = computed(() => {
    const role = this.session.user()?.role;
    return role === 'DealerOwner' || role === 'DealerEmployee';
  });

  private dealerUrl(path = ''): string | undefined {
    return this.isDealer() ? `${this.base}${path}` : undefined;
  }

  /** The dealership. Loaded once per shell; every dealer screen reads it (locked state, owner-ness). */
  readonly me = httpResource<DealerProfile>(() => this.dealerUrl());

  readonly dashboard = httpResource<DealerDashboard>(() => this.dealerUrl('/dashboard'));

  readonly period = signal<ReportPeriod>('monthly');
  readonly report = httpResource<DealerReport>(() => {
    const url = this.dealerUrl('/reports');
    return url ? { url, params: { period: this.period() } } : undefined;
  });

  readonly activityPage = signal(1);
  readonly activity = httpResource<PagedResult<DealerActivityEntry>>(() => {
    const url = this.dealerUrl('/activity');
    return url ? { url, params: { page: this.activityPage(), pageSize: 25 } } : undefined;
  });

  readonly employees = httpResource<readonly Employee[]>(() => this.dealerUrl('/employees'));

  readonly delivery = httpResource<DeliverySettingsView>(() => this.dealerUrl('/delivery'));

  // ── Staff (spec 4.2) ──

  invite(request: InviteEmployeeRequest): Promise<Employee> {
    return firstValueFrom(this.http.post<Employee>(`${this.base}/employees`, request));
  }

  resendInvitation(employeeId: string): Promise<Employee> {
    return firstValueFrom(
      this.http.post<Employee>(`${this.base}/employees/${employeeId}/resend-invitation`, {}),
    );
  }

  setReportAccess(employeeId: string, canViewReports: boolean): Promise<Employee> {
    return firstValueFrom(
      this.http.put<Employee>(`${this.base}/employees/${employeeId}/report-access`, {
        canViewReports,
      }),
    );
  }

  deactivate(employeeId: string): Promise<Employee> {
    return firstValueFrom(
      this.http.post<Employee>(`${this.base}/employees/${employeeId}/deactivate`, {}),
    );
  }

  reactivate(employeeId: string): Promise<Employee> {
    return firstValueFrom(
      this.http.post<Employee>(`${this.base}/employees/${employeeId}/reactivate`, {}),
    );
  }

  // ── The application itself (spec 3.1, step two) ──

  /**
   * Submits the gallery for the platform's licence check.
   *
   * `POST /api/v1/dealers`, not `/dealers/me`: there is no `me` to address yet — this is the call
   * that brings the dealership into existence, against the owner id on the token. Multipart, because
   * the three licence documents go up with the details in one request; the aggregate refuses a
   * partial application, so there is no half-submitted state to recover from.
   *
   * `Content-Type` is deliberately unset. Naming it would send a multipart header with no boundary
   * and the server would parse nothing.
   */
  async submitApplication(form: FormData): Promise<DealerProfile> {
    const token = await firstValueFrom(
      this.http.get<{ requestToken: string }>('/bff/antiforgery'),
    );
    const dealer = await firstValueFrom(
      this.http.post<DealerProfile>('/api/v1/dealers', form, {
        headers: { 'X-XSRF-TOKEN': token.requestToken },
      }),
    );
    // The gate reads `me`, and it currently holds the 404 that sent the owner here.
    this.me.reload();
    return dealer;
  }

  // ── The dealer page (spec 4.1) ──

  updateProfile(request: UpdateProfileRequest): Promise<DealerProfile> {
    return firstValueFrom(this.http.put<DealerProfile>(`${this.base}/profile`, request));
  }

  /** Request a URL, PUT the bytes, confirm — the same three steps as a car photo. */
  async uploadBranding(kind: 'logo' | 'cover', file: File): Promise<DealerProfile> {
    const ticket = await firstValueFrom(
      this.http.post<BrandingUpload>(`${this.base}/branding/${kind}/upload-url`, {
        contentType: file.type,
      }),
    );
    await firstValueFrom(
      this.http.put(ticket.uploadUrl, file, { headers: { 'Content-Type': file.type } }),
    );
    return firstValueFrom(
      this.http.put<DealerProfile>(`${this.base}/branding/${kind}`, {
        storageKey: ticket.storageKey,
      }),
    );
  }

  // ── Delivery (spec 4.4) ──

  /** `fee` is required to switch delivery on and ignored when switching it off. */
  updateDelivery(isEnabled: boolean, radiusKm: number, fee: number | null): Promise<DealerProfile> {
    return firstValueFrom(
      this.http.put<DealerProfile>(`${this.base}/delivery`, { isEnabled, radiusKm, fee }),
    );
  }

  refreshMe(): void {
    this.me.reload();
  }

  /**
   * Re-reads the two resources here that are derived from OTHER screens' data.
   *
   * The dashboard's tiles and the activity trail are built from bookings and the fleet, and both are
   * fetched once when their screen first loads. Nothing reloaded them after a decision taken
   * elsewhere in the console, so an owner who approved a request was still told "8 pending" by their
   * own dashboard, and the Activity screen still omitted the approval, for the rest of the session:
   * their own action, missing from their own audit trail. Called by the bookings and fleet services
   * after any write, because a figure on a screen you are not looking at is the one that goes stale
   * unnoticed.
   */
  refreshDerived(): void {
    this.dashboard.reload();
    this.activity.reload();
  }
}
