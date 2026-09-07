import { HttpClient, httpResource } from '@angular/common/http';
import { Injectable, Injector, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter, firstValueFrom, map, startWith } from 'rxjs';
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
import { loaded } from './loaded';
import { SessionService } from './session.service';

/**
 * What the signed-in member of staff may do, one field per API policy.
 *
 * Written down once, here, because it IS the controllers' authorize attributes and drifting from
 * them is how a console grows buttons that only ever 403. Owner-ness alone is not the answer to any
 * of these: three of them also require a dealership that may trade, one is a per-person grant, and
 * one belongs to every active member of staff.
 */
export interface DealerPermissions {
  readonly isOwner: boolean;
  readonly canTrade: boolean;
  /** `DealerOwner`: `PUT me/profile`, the branding endpoints. Not gated on trading. */
  readonly canEditProfile: boolean;
  /** `ApprovedDealer`: every vehicle write — create, edit, status, delete, images. */
  readonly canManageFleet: boolean;
  /** `ApprovedDealer`: the entire employees controller, `GET` included. */
  readonly canManageStaff: boolean;
  /** `ApprovedDealer`: `PUT me/delivery`. Reading it needs only membership. */
  readonly canEditDelivery: boolean;
  /** `ApprovedDealerStaff`: approve and reject. Pickup and return need only membership. */
  readonly canDecideBookings: boolean;
  /** The owner always; an employee only where the owner granted it (spec 4.2). */
  readonly canViewReports: boolean;
}

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
  private readonly injector = inject(Injector);
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

  /**
   * Bumped on every completed navigation, the same way the rail's workload counts are.
   *
   * `me` used to be fetched once per shell, and everything now hangs off it: the rail, the gate,
   * whether the reports request is sent at all, and which controls each screen renders. Fetched once
   * meant an employee granted report access mid-shift went on being told it was not theirs, and a
   * dealership suspended under a member of staff went on showing them controls that had stopped
   * working — until some unrelated write happened to call `refreshMe`. One small request per screen
   * change is the price of the rail telling the truth.
   */
  private readonly navigation = toSignal(
    inject(Router).events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map((_, index) => index + 1),
      startWith(0),
    ),
    { initialValue: 0 },
  );

  /** The dealership, and who is asking: every dealer screen reads it (locked state, permissions). */
  readonly me = httpResource<DealerProfile>(() => {
    // Read so the resource re-runs on navigation; the value is not part of the request.
    this.navigation();
    return this.dealerUrl();
  });

  /** Guarded: `value()` throws in the error state, so the permissions below never read it directly. */
  private readonly dealer = loaded(this.me);

  /**
   * What this member of staff may do, as the API's own policy table (spec 1.5's role-based views).
   *
   * THREE answers, not two — `null` means "not asked yet", and every consumer has to branch on it
   * separately. Reading unknown as false flashes the owner's own controls in a beat late and bounces
   * them off `/dealer/fleet/new` on a cold load; reading it as true shows an employee buttons that
   * are about to disappear. Same lesson `DealerGateComponent` wrote down about four answers.
   *
   * Derived from `GET /dealers/me`, not from the session role, because `canViewReports` and
   * `canTrade` exist nowhere else: report access is a per-person grant the owner flips (spec 4.2),
   * so it cannot be inferred from being an employee.
   *
   * These are HINTS. Every endpoint enforces the same answer server-side, and the DTO says so. What
   * they buy is that the console can decide BEFORE the click — which matters more than it sounds,
   * because a role failure returns a bodiless 403 (`ProblemDetailsAuthorizationResultHandler` writes
   * ProblemDetails only when the approved-dealer handler is the one that failed), and every screen
   * renders a bodiless failure as "The service did not respond" — the platform looking broken to
   * someone who simply is not the owner.
   */
  readonly permissions = computed<DealerPermissions | null>(() => {
    const dealer = this.dealer();
    if (!dealer) return null;

    const { isOwner, canTrade } = dealer;
    return {
      isOwner,
      canTrade,
      // DealerOwner, and deliberately NOT gated on trading: an owner fixing a rejected application
      // edits their page to fix it. `PUT me/profile` and the branding endpoints agree.
      canEditProfile: isOwner,
      // ApprovedDealer on every vehicle write, on `PUT me/delivery`, and on the WHOLE employees
      // controller — an employee 403s there even on GET, which is why the staff list is gated too.
      canManageFleet: isOwner && canTrade,
      canManageStaff: isOwner && canTrade,
      canEditDelivery: isOwner && canTrade,
      // ApprovedDealerStaff: an employee approves and rejects requests, and that is their default
      // permission (spec 4.2). Recording a pickup or a return needs only membership, so it is not
      // here — an approved rental is honoured even by a dealership that can no longer trade.
      canDecideBookings: canTrade,
      canViewReports: dealer.canViewReports,
    };
  });

  readonly dashboard = httpResource<DealerDashboard>(() => this.dealerUrl('/dashboard'));

  readonly period = signal<ReportPeriod>('monthly');
  /**
   * Not requested at all without the grant, rather than requested and refused.
   *
   * The screen reads the same permission to say so, because once this resource stays idle no 403
   * ever arrives and a screen waiting for one waits forever. Reading `permissions()` here also means
   * a grant that lands on a later `me` reload fires the request by itself.
   */
  readonly report = httpResource<DealerReport>(() => {
    const url = this.dealerUrl('/reports');
    if (!url || !this.permissions()?.canViewReports) return undefined;
    return { url, params: { period: this.period() } };
  });

  readonly activityPage = signal(1);
  readonly activity = httpResource<PagedResult<DealerActivityEntry>>(() => {
    const url = this.dealerUrl('/activity');
    return url ? { url, params: { page: this.activityPage(), pageSize: 25 } } : undefined;
  });

  /**
   * The caller's OWN actions, filtered by the server (`?actor=me`).
   *
   * A separate resource rather than a filter over `activity`: that one is paged 25 at a time, so
   * filtering it here would drop everything past the first page and quietly show an incomplete
   * record of what someone did. Small on purpose — this feeds one panel.
   */
  readonly myActivity = httpResource<PagedResult<DealerActivityEntry>>(() => {
    const url = this.dealerUrl('/activity');
    return url ? { url, params: { page: 1, pageSize: 6, actor: 'me' } } : undefined;
  });

  /**
   * Owner-only, and the whole controller is: an employee is refused this list even to READ it, and
   * so is an owner whose dealership cannot yet trade. Asking anyway fired a 403 on every page load
   * for both of them, and told the pending owner "only the dealer owner can manage staff" — the one
   * explanation that could not be true of the person reading it.
   */
  readonly employees = httpResource<readonly Employee[]>(() =>
    this.permissions()?.canManageStaff ? this.dealerUrl('/employees') : undefined,
  );

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
    const token = await firstValueFrom(this.http.get<{ requestToken: string }>('/bff/antiforgery'));
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
   * The dealership's standing, awaited — what a guard needs and a signal cannot give it.
   *
   * Costs no extra request: `me` is already loading for the gate, and this only waits for the
   * answer it was going to get anyway. Resolves `null` when the call failed, which the guards read
   * as "let the route through" so that `DealerGateComponent` stays the single place that explains a
   * dealership whose standing could not be read.
   */
  async standing(): Promise<DealerProfile | null> {
    if (this.me.hasValue()) return this.me.value();

    await new Promise<void>((resolve) => {
      const stop = effect(
        () => {
          const status = this.me.status();
          if (status !== 'loading' && status !== 'reloading') {
            resolve();
            // Freed on the next microtask: destroying an effect from inside its own run throws.
            queueMicrotask(() => stop.destroy());
          }
        },
        { injector: this.injector },
      );
    });

    return this.me.hasValue() ? this.me.value() : null;
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
