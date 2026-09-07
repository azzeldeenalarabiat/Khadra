import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { TranslationKey } from '../../core/i18n/en';
import { I18nService } from '../../core/i18n/i18n.service';
import { IconName } from '../../shared/icon/icon-paths';
import { IconComponent } from '../../shared/icon/icon.component';

/** Keys, not words: this screen explains itself in whichever language the console is speaking. */
interface Missing {
  readonly titleKey: TranslationKey;
  readonly icon: IconName;
  /** What this screen will show once the data behind it exists. */
  readonly purposeKey: TranslationKey;
  /** What is actually missing, named precisely enough to act on. */
  readonly blockedKey: TranslationKey;
  /** What already exists in its place today, if anything. */
  readonly instead?: {
    readonly textKey: TranslationKey;
    readonly labelKey: TranslationKey;
    readonly route: string;
  };
}

/**
 * A console screen whose data does not exist yet.
 *
 * Every one of these was drawn in the Admin Console design and built against sample content. The
 * sample content is gone: a screen full of invented dealers, payouts and audit entries is worse
 * than an empty one, because an administrator cannot tell which figures are real, and the first
 * person to trust one of them finds out the hard way.
 *
 * So each screen now says what it will show, what is missing, and where the real thing lives today.
 * When the context behind it ships, the route swaps to a real component and this entry goes.
 */
@Component({
  selector: 'kh-not-built',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './not-built.component.html',
  imports: [RouterLink, IconComponent],
})
export class NotBuiltComponent {
  private readonly route = inject(ActivatedRoute);

  private readonly key = toSignal(this.route.data.pipe(map((data) => data['missing'] as string)), {
    initialValue: this.route.snapshot.data['missing'] as string,
  });

  protected readonly t = inject(I18nService).t;

  protected readonly copy = computed<Missing>(() => SCREENS[this.key()] ?? FALLBACK);
}

const FALLBACK: Missing = {
  titleKey: 'notBuilt.fallback.title',
  icon: 'info',
  purposeKey: 'notBuilt.fallback.purpose',
  blockedKey: 'notBuilt.fallback.blocked',
};

/**
 * Keyed by the route's `missing` value. The wording is deliberately specific: "the Payments context
 * is not built" tells the next person what to do, where "coming soon" tells them nothing.
 */
const SCREENS: Readonly<Record<string, Missing>> = {
  payments: {
    titleKey: 'nav.payments',
    icon: 'currency-circle-dollar',
    purposeKey: 'notBuilt.payments.purpose',
    blockedKey: 'notBuilt.payments.blocked',
  },
  'payment-detail': {
    titleKey: 'screen.paymentDetails',
    icon: 'currency-circle-dollar',
    purposeKey: 'notBuilt.paymentDetail.purpose',
    blockedKey: 'notBuilt.paymentDetail.blocked',
  },
  payouts: {
    titleKey: 'nav.payouts',
    icon: 'currency-circle-dollar',
    purposeKey: 'notBuilt.payouts.purpose',
    blockedKey: 'notBuilt.payouts.blocked',
    instead: {
      textKey: 'notBuilt.payouts.instead',
      labelKey: 'nav.dealers',
      route: '/dealers',
    },
  },
  finance: {
    titleKey: 'nav.finance',
    icon: 'chart-line-up',
    purposeKey: 'notBuilt.finance.purpose',
    blockedKey: 'notBuilt.finance.blocked',
  },
  reviews: {
    titleKey: 'nav.reviews',
    icon: 'star',
    purposeKey: 'notBuilt.reviews.purpose',
    blockedKey: 'notBuilt.reviews.blocked',
  },
  notifications: {
    titleKey: 'nav.notifications',
    icon: 'bell',
    purposeKey: 'notBuilt.notifications.purpose',
    blockedKey: 'notBuilt.notifications.blocked',
    instead: {
      textKey: 'notBuilt.notifications.instead',
      labelKey: 'nav.dashboard',
      route: '/dashboard',
    },
  },
};
