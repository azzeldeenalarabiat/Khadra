import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { KeyValue } from '../../core/models/console.models';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { TranslationKey } from '../../core/i18n/en';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { Language } from '../../core/i18n/language';
import { ProblemSnapshot, serverSentence, snapshotProblem } from '../../core/i18n/problem';

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
  private readonly i18n = inject(I18nService);
  protected readonly t = this.i18n.t;
  private readonly formats = inject(FormatService);
  protected readonly resource = httpResource<BusinessRulesView>(
    () => '/api/v1/admin/settings/business-rules',
  );

  private readonly view = loaded(this.resource);

  protected readonly source = computed(() => this.view()?.source ?? '');

  protected readonly failure = computed(() => {
    const error = this.resource.error();
    if (!error) return null;
    return describe(snapshotProblem(error), this.t, this.i18n.lang());
  });

  protected readonly moneyRows = computed<readonly KeyValue[]>(() => {
    const rules = this.view()?.rules;
    if (!rules) return [];
    return [
      {
        k: this.t('dealerReports.platformCommission'),
        v: this.formats.percent(rules.commissionPercent),
      },
      {
        k: this.t('settings.bookingDeposit'),
        v: this.t('settings.percentOfTheRentalTotal', {
          percent: this.formats.percent(rules.depositPercent),
        }),
      },
      // No delivery fee row: it is no longer a platform number. Each gallery sets its own on its
      // Delivery page, so there is no single figure this screen could honestly print.
    ];
  });

  protected readonly windowRows = computed<readonly KeyValue[]>(() => {
    const rules = this.view()?.rules;
    if (!rules) return [];
    // A length, not a countdown: each is the window itself, so it is a plural message rather than
    // one of FormatService's clocks.
    const hours = (count: number): string => this.t('adminBooking.hours', { count });
    return [
      { k: this.t('myBooking.paymentWindow'), v: hours(rules.paymentWindowHours) },
      {
        k: this.t('myBooking.freeCancellationWindow'),
        v: this.t('settings.minutes', { count: rules.freeCancellationWindowMinutes }),
      },
      { k: this.t('myBooking.noShowTimeout'), v: hours(rules.noShowTimeoutHours) },
      {
        k: this.t('myBooking.settlementWindowAfterReturn'),
        v: hours(rules.postReturnSettlementHours),
      },
      { k: this.t('settings.dealerApplicationReviewSla'), v: hours(rules.adminSlaHours) },
    ];
  });

  protected readonly penaltyRows = computed<readonly KeyValue[]>(() => {
    const rules = this.view()?.rules;
    if (!rules) return [];
    return [
      {
        k: this.t('settings.customerCancelsAfterThe'),
        v: this.t('settings.percentOfTheDeposit', {
          percent: this.formats.percent(rules.customerCancellationPenaltyPercent),
        }),
      },
      {
        k: this.t('settings.dealerFailsToDeliver'),
        // One run, so Arabic cannot lay the two bounds out upper bound first.
        v: this.t('settings.percentOfTheRental', {
          percent: this.formats.percentRange(
            rules.dealerNonDeliveryPenaltyMinPercent,
            rules.dealerNonDeliveryPenaltyMaxPercent,
          ),
        }),
      },
    ];
  });

  protected readonly renterRows = computed<readonly KeyValue[]>(() => {
    const rules = this.view()?.rules;
    if (!rules) return [];
    return [
      {
        k: this.t('settings.minimumRenterAge'),
        // Null is a real state: nobody is refused on age. Printing a number here would invent one.
        // When there is an age it is the server's, shown bare — the row's own key names the unit,
        // and adding a "years" word would be this screen writing copy nobody decided on.
        v:
          rules.minimumRenterAge === null
            ? this.t('settings.notSetNobodyIs')
            : this.formats.number(rules.minimumRenterAge),
      },
    ];
  });

  protected reload(): void {
    this.resource.reload();
  }
}

function describe(
  problem: ProblemSnapshot,
  t: (key: TranslationKey) => string,
  language: Language,
): string {
  if (problem.status === 403) return t('settings.platformSettingsAreFor');
  return serverSentence(problem, language, t) ?? t('settings.thePlatformSettingsCould');
}
