import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { DealerConsoleService } from '../../core/services/dealer-console.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { I18nService } from '../../core/i18n/i18n.service';
import { FormatService } from '../../core/i18n/format.service';

interface OwnerOnlyRow {
  readonly label: string;
  readonly value: string;
}

/**
 * My business (design: Employee Console, `isMyBusiness`).
 *
 * The dealership as its staff need to know it, and nothing more. An employee reads `GET /dealers/me`
 * — the same call the owner's console makes — but every write on that aggregate is owner-only, so
 * this is a page rather than a form. That is a better answer than the owner's profile editor with
 * every input disabled: there is no Save to look for, and nothing to wonder whether you broke.
 *
 * The design draws several fields the platform does not hold, and they are NOT invented here:
 *   · a street address — `Dealer` stores a `GeoPoint`, latitude and longitude, and nothing else;
 *   · a dealership phone and email — the aggregate has neither;
 *   · the owner's name — it lives in IdentityAccess, and Dealers may reference it only by id;
 *   · "4.8 · 96 reviews" — the Reviews context is not built.
 * Each is named as missing rather than filled with something plausible, because a plausible phone
 * number on a screen is indistinguishable from a real one to whoever dials it.
 */
@Component({
  selector: 'kh-employee-business',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './employee-business.component.html',
  imports: [IconComponent],
})
export class EmployeeBusinessComponent {
  protected readonly t = inject(I18nService).t;
  private readonly service = inject(DealerConsoleService);
  protected readonly fmt = inject(FormatService);

  protected readonly resource = this.service.me;
  /** Guarded: `value()` throws in the error state, so nothing reads the resource directly. */
  private readonly data = loaded(this.resource);
  protected readonly dealer = computed(() => this.data() ?? null);

  protected readonly failure = computed(() => {
    const error = this.resource.error() as
      { status?: number; error?: { code?: string } } | undefined;
    if (!error) return null;
    if (error.error?.code === 'dealer.not_registered')
      return 'This account is not part of a dealership.';
    return "Your dealership's details could not be loaded. Nothing has been changed.";
  });

  /** The days, in the week's order, as the server sent them. */
  protected readonly hours = computed(() => this.dealer()?.operatingHours ?? []);

  /**
   * What only the owner can change, listed so an employee knows where the edge is.
   *
   * Every value is real. Delivery shows the gallery's OWN fee, which lives on the dealership since
   * the owner moved it there — there is no platform-wide figure to fall back on, and the record
   * carries `null` exactly when delivery is off.
   */
  protected readonly ownerOnly = computed<readonly OwnerOnlyRow[]>(() => {
    const d = this.dealer();
    if (!d) return [];

    const delivery = d.delivery.isEnabled
      ? d.delivery.fee
        ? `${d.delivery.radiusKm} km · ${d.delivery.fee.amount} ${d.delivery.fee.currency}`
        : `${d.delivery.radiusKm} km`
      : 'Not offered';

    return [
      { label: this.t('dealerProfile.commercialRegistration'), value: d.commercialRegistrationNumber },
      { label: this.t('employeeBusiness.verificationStatus'), value: d.verificationStatus },
      { label: 'Delivery', value: delivery },
      { label: this.t('employeeBusiness.businessNameAndLocation'), value: 'Owner-maintained' },
      { label: this.t('employeeBusiness.staffAndPermissions'), value: 'Owner-maintained' },
      { label: this.t('employeeBusiness.financialSettings'), value: 'Not shown to staff' },
    ];
  });

  /** Opening hours read from the record; a closed day says so rather than showing an empty range. */
  protected schedule(day: {
    isClosed: boolean;
    opensAt: string | null;
    closesAt: string | null;
  }): string {
    if (day.isClosed || !day.opensAt || !day.closesAt) return 'Closed';
    return `${day.opensAt.slice(0, 5)} – ${day.closesAt.slice(0, 5)}`;
  }

  protected readonly since = computed(() => {
    const d = this.dealer();
    if (!d) return '';
    return new Date(d.createdAt).toLocaleDateString('en-GB', {
      day: 'numeric',
      month: 'long',
      year: 'numeric',
    });
  });

  protected reload(): void {
    this.resource.reload();
  }
}
