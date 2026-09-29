import { httpResource } from '@angular/common/http';
import { Injectable, signal } from '@angular/core';
import { PagedResult } from '../models/bookings.api';
import { OfficePayable, OfficePayouts, OfficeSettlement, OfficeSettlementDetail } from '../models/payables.api';
import { PayableScope } from './admin-payables.service';

/**
 * The office's own payouts (payments Phase 8): what Khadra owes it — or it owes Khadra — per currency, the bookings
 * behind that, and the settlements recorded. Read only: settling is Khadra's, recorded by an administrator. The owner
 * and an employee granted the reports read it; anyone else is answered 403, which the screen explains.
 */
@Injectable({ providedIn: 'root' })
export class DealerPayoutsService {
  private readonly base = '/api/v1/dealers/me/payouts';

  private readonly showing = signal(false);
  readonly scope = signal<PayableScope>('open');
  readonly payablesPage = signal(1);
  readonly settlementsPage = signal(1);
  readonly viewingSettlement = signal<string | null>(null);

  readonly balances = httpResource<OfficePayouts>(() => (this.showing() ? this.base : undefined));

  readonly payables = httpResource<PagedResult<OfficePayable>>(() =>
    this.showing()
      ? { url: `${this.base}/payables`, params: { scope: this.scope(), page: this.payablesPage(), pageSize: 25 } }
      : undefined,
  );

  readonly settlements = httpResource<PagedResult<OfficeSettlement>>(() =>
    this.showing() ? { url: `${this.base}/settlements`, params: { page: this.settlementsPage(), pageSize: 25 } } : undefined,
  );

  readonly settlement = httpResource<OfficeSettlementDetail>(() => {
    const id = this.showing() ? this.viewingSettlement() : null;
    return id ? `${this.base}/settlements/${id}` : undefined;
  });

  show(on: boolean): void {
    this.showing.set(on);
  }
}
