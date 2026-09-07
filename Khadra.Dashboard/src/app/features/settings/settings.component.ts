import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { KeyValue } from '../../core/models/console.models';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { I18nService } from '../../core/i18n/i18n.service';

interface BusinessRules {
  readonly commissionPercent: number;
  readonly depositPercent: number;
  readonly noShowTimeoutHours: number;
  readonly dealerNonDeliveryPenaltyMinPercent: number;
  readonly dealerNonDeliveryPenaltyMaxPercent: number;
  readonly freeCancellationWindowMinutes: number;
  readonly adminSlaHours: number;
  readonly customerCancellationPenaltyPercent: number;
  readonly paymentWindowHours: number;
  readonly postReturnSettlementHours: number;
  /** Null means the owner has not set one and nobody is refused on age — a real shipping state. */
  readonly minimumRenterAge: number | null;
}

interface BusinessRulesView {
  readonly rules: BusinessRules;
  readonly source: string;
  readonly isEditable: boolean;
}

/**
 * The numbers the whole platform runs on (spec 2).
 *
 * Read-only, and it says why. They come from configuration; the aggregate that would make them
 * editable is not wired to a table, does not carry the same fields, and two of these values are
 * still open owner decisions — an editable form here would let an administrator settle a business
 * question by typing into a box.
 *
 * Every figure is the server's. The design draws rows for a late-return charge, how long a licence
 * is held and a foreign-customer deposit; none of the three exists in the domain, so none is drawn.
 */
@Component({
  selector: 'kh-settings',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './settings.component.html',
  imports: [IconComponent],
})
export class SettingsComponent {
  protected readonly t = inject(I18nService).t;
  protected readonly resource = httpResource<BusinessRulesView>(
    () => '/api/v1/admin/settings/business-rules',
  );

  private readonly view = loaded(this.resource);

  protected readonly source = computed(() => this.view()?.source ?? '');

  protected readonly failure = computed(() => {
    const error = this.resource.error() as { status?: number } | undefined;
    if (!error) return null;
    if (error.status === 403) return 'Platform settings are for administrators.';
    return 'The platform settings could not be loaded. Nothing has been changed.';
  });

  protected readonly moneyRows = computed<readonly KeyValue[]>(() => {
    const rules = this.view()?.rules;
    if (!rules) return [];
    return [
      { k: 'Platform commission', v: `${rules.commissionPercent}%` },
      { k: 'Booking deposit', v: `${rules.depositPercent}% of the rental total` },
      // No delivery fee row: it is no longer a platform number. Each gallery sets its own on its
      // Delivery page, so there is no single figure this screen could honestly print.
    ];
  });

  protected readonly windowRows = computed<readonly KeyValue[]>(() => {
    const rules = this.view()?.rules;
    if (!rules) return [];
    return [
      { k: 'Payment window', v: `${rules.paymentWindowHours} hours` },
      { k: 'Free cancellation window', v: `${rules.freeCancellationWindowMinutes} minutes` },
      { k: 'No-show timeout', v: `${rules.noShowTimeoutHours} hours` },
      { k: 'Settlement window after return', v: `${rules.postReturnSettlementHours} hours` },
      { k: 'Dealer application review SLA', v: `${rules.adminSlaHours} hours` },
    ];
  });

  protected readonly penaltyRows = computed<readonly KeyValue[]>(() => {
    const rules = this.view()?.rules;
    if (!rules) return [];
    return [
      {
        k: 'Customer cancels after the free window',
        v: `${rules.customerCancellationPenaltyPercent}% of the deposit`,
      },
      {
        k: 'Dealer fails to deliver',
        v: `${rules.dealerNonDeliveryPenaltyMinPercent}–${rules.dealerNonDeliveryPenaltyMaxPercent}% of the rental`,
      },
    ];
  });

  protected readonly renterRows = computed<readonly KeyValue[]>(() => {
    const rules = this.view()?.rules;
    if (!rules) return [];
    return [
      {
        k: 'Minimum renter age',
        // Null is a real state: nobody is refused on age. Printing a number here would invent one.
        v:
          rules.minimumRenterAge === null
            ? 'Not set — nobody is refused on age'
            : `${rules.minimumRenterAge}`,
      },
    ];
  });

  protected reload(): void {
    this.resource.reload();
  }
}
