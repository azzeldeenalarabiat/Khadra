import { HttpClient, httpResource } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
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

  /** The dealership. Loaded once per shell; every dealer screen reads it (locked state, owner-ness). */
  readonly me = httpResource<DealerProfile>(() => this.base);

  readonly dashboard = httpResource<DealerDashboard>(() => `${this.base}/dashboard`);

  readonly period = signal<ReportPeriod>('monthly');
  readonly report = httpResource<DealerReport>(() => ({
    url: `${this.base}/reports`,
    params: { period: this.period() },
  }));

  readonly activityPage = signal(1);
  readonly activity = httpResource<PagedResult<DealerActivityEntry>>(() => ({
    url: `${this.base}/activity`,
    params: { page: this.activityPage(), pageSize: 25 },
  }));

  readonly employees = httpResource<readonly Employee[]>(() => `${this.base}/employees`);

  readonly delivery = httpResource<DeliverySettingsView>(() => `${this.base}/delivery`);

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

  updateDelivery(isEnabled: boolean, radiusKm: number): Promise<DealerProfile> {
    return firstValueFrom(
      this.http.put<DealerProfile>(`${this.base}/delivery`, { isEnabled, radiusKm }),
    );
  }

  refreshMe(): void {
    this.me.reload();
  }
}
