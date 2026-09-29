import { HttpClient, httpResource } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PagedResult } from '../models/bookings.api';
import {
  FinanceSummary,
  OfficeBalance,
  OfficePayable,
  OfficeSettlement,
  OfficeSettlementDetail,
  PayableHold,
} from '../models/payables.api';

/** Which of an office's payables a list shows. */
export type PayableScope = 'open' | 'settled' | 'all';

/** What the administrator confirms when recording a settlement: the balance they were shown, and how it moved. */
export interface SettlementRequest {
  readonly currency: string;
  readonly provider: string;
  /** The balance due as the screen showed it, signed from Khadra's side. */
  readonly expectedAmount: number;
  /** The Amman day the money moved, `yyyy-MM-dd`. */
  readonly paidOn: string;
  readonly reference: string | null;
  readonly note: string | null;
}

/**
 * The office payables ledger, as the administrator works it (payments Phase 8): every office's balance, one office's
 * payables and settlements, one settlement, and Khadra's finance figures — and the four actions, recording a
 * settlement, voiding one, and holding or releasing a payable. Every read is idle until a screen asks for it.
 */
@Injectable({ providedIn: 'root' })
export class AdminPayablesService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/admin';

  // ── Payouts: every office ─────────────────────────────────────────────────────────────────────

  private readonly showingBalances = signal(false);

  readonly balances = httpResource<OfficeBalance[]>(() =>
    this.showingBalances() ? `${this.base}/office-balances` : undefined,
  );

  /** Bookings a hold stops being recorded, platform-wide: their records need a person. */
  readonly bookingHolds = httpResource<PayableHold[]>(() =>
    this.showingBalances() ? `${this.base}/office-payables/holds` : undefined,
  );

  // ── One office ────────────────────────────────────────────────────────────────────────────────

  /** The office a page is showing; null keeps its resources idle. */
  readonly office = signal<string | null>(null);
  readonly scope = signal<PayableScope>('open');
  readonly payablesPage = signal(1);
  readonly settlementsPage = signal(1);

  readonly officeBalances = httpResource<OfficeBalance[]>(() => {
    const dealerId = this.office();
    return dealerId ? { url: `${this.base}/office-balances`, params: { dealerId } } : undefined;
  });

  readonly officePayables = httpResource<PagedResult<OfficePayable>>(() => {
    const dealerId = this.office();
    return dealerId
      ? { url: `${this.base}/office-payables`, params: { dealerId, scope: this.scope(), page: this.payablesPage(), pageSize: 25 } }
      : undefined;
  });

  readonly officeHolds = httpResource<PayableHold[]>(() => {
    const dealerId = this.office();
    return dealerId ? { url: `${this.base}/office-payables/holds`, params: { dealerId } } : undefined;
  });

  readonly officeSettlements = httpResource<PagedResult<OfficeSettlement>>(() => {
    const dealerId = this.office();
    return dealerId
      ? { url: `${this.base}/offices/${dealerId}/settlements`, params: { page: this.settlementsPage(), pageSize: 25 } }
      : undefined;
  });

  // ── One settlement ────────────────────────────────────────────────────────────────────────────

  readonly viewingSettlement = signal<string | null>(null);

  readonly settlement = httpResource<OfficeSettlementDetail>(() => {
    const id = this.viewingSettlement();
    return id ? `${this.base}/office-settlements/${id}` : undefined;
  });

  // ── Finance ───────────────────────────────────────────────────────────────────────────────────

  private readonly showingFinance = signal(false);
  /** Amman days, `yyyy-MM-dd`; empty is the server's default, the current month. */
  readonly financeRange = signal<{ readonly from: string; readonly to: string }>({ from: '', to: '' });
  readonly reportPage = signal(1);

  private readonly financeParams = computed(() => {
    const params: Record<string, string> = {};
    const { from, to } = this.financeRange();
    if (from) params['from'] = from;
    if (to) params['to'] = to;
    return params;
  });

  readonly finance = httpResource<FinanceSummary>(() =>
    this.showingFinance() ? { url: `${this.base}/finance/summary`, params: this.financeParams() } : undefined,
  );

  /** The commission report: every payable whose outcome became final in the span the summary answered for. */
  readonly report = httpResource<PagedResult<OfficePayable>>(() => {
    const summary = this.showingFinance() ? this.finance.value() : undefined;
    return summary
      ? {
          url: `${this.base}/office-payables`,
          params: { scope: 'all', from: summary.from, to: summary.to, page: this.reportPage(), pageSize: 25 },
        }
      : undefined;
  });

  /** Which screen is on; the others stay idle. */
  show(view: 'payouts' | 'finance' | null): void {
    this.showingBalances.set(view === 'payouts');
    this.showingFinance.set(view === 'finance');
  }

  setFinanceRange(from: string, to: string): void {
    this.financeRange.set({ from, to });
    this.reportPage.set(1);
  }

  // ── Actions ───────────────────────────────────────────────────────────────────────────────────

  /**
   * Records that everything due to or from the office in one currency and kind of money moved by hand on `paidOn`,
   * netted, audited, under a new number. Rejects with the server's refusal — `payables.balance_changed` carries the
   * balance due now.
   */
  async recordSettlement(dealerId: string, request: SettlementRequest): Promise<OfficeSettlementDetail> {
    return this.post<OfficeSettlementDetail>(`${this.base}/offices/${dealerId}/settlements`, request);
  }

  /** Voids a settlement recorded wrongly: its payables are due again. */
  async voidSettlement(settlementId: string, reason: string): Promise<OfficeSettlementDetail> {
    return this.post<OfficeSettlementDetail>(`${this.base}/office-settlements/${settlementId}/void`, { reason });
  }

  /** Leaves one payable out of settlements, with the administrator's reason. */
  async hold(payableId: string, reason: string): Promise<OfficePayable> {
    return this.post<OfficePayable>(`${this.base}/office-payables/${payableId}/hold`, { reason });
  }

  /** Lets a payable an administrator held back into the next settlement. */
  async release(payableId: string, note: string | null): Promise<OfficePayable> {
    return this.post<OfficePayable>(`${this.base}/office-payables/${payableId}/release`, { note });
  }

  /** Every read of one office again, after an action changed it. */
  reloadOffice(): void {
    this.officeBalances.reload();
    this.officePayables.reload();
    this.officeHolds.reload();
    this.officeSettlements.reload();
  }

  private async post<T>(url: string, body: unknown): Promise<T> {
    const token = await firstValueFrom(this.http.get<{ requestToken: string }>('/bff/antiforgery'));
    return firstValueFrom(this.http.post<T>(url, body, { headers: { 'X-XSRF-TOKEN': token.requestToken } }));
  }
}
