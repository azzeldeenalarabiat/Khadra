import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { IconName } from '../../shared/icon/icon-paths';
import { IconComponent } from '../../shared/icon/icon.component';

interface Missing {
  readonly title: string;
  readonly icon: IconName;
  /** What this screen will show once the data behind it exists. */
  readonly purpose: string;
  /** What is actually missing, named precisely enough to act on. */
  readonly blocked: string;
  /** What already exists in its place today, if anything. */
  readonly instead?: { readonly text: string; readonly label: string; readonly route: string };
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

  protected readonly copy = computed<Missing>(() => SCREENS[this.key()] ?? FALLBACK);
}

const FALLBACK: Missing = {
  title: 'Not built yet',
  icon: 'info',
  purpose: 'This screen is part of the design but has no data behind it yet.',
  blocked: 'The context that would supply it has not been built.',
};

/**
 * Keyed by the route's `missing` value. The wording is deliberately specific: "the Payments context
 * is not built" tells the next person what to do, where "coming soon" tells them nothing.
 */
const SCREENS: Readonly<Record<string, Missing>> = {
  payments: {
    title: 'Payments',
    icon: 'currency-circle-dollar',
    purpose: 'Every deposit, balance, refund and penalty the platform has processed.',
    blocked:
      'The Payments context is not built. It is blocked on owner decisions (provider, the non-delivery penalty tier, the quick-cancellation fee), and no money has ever moved through the platform.',
  },
  'payment-detail': {
    title: 'Payment details',
    icon: 'currency-circle-dollar',
    purpose: 'One transaction, its provider reference, and the booking it belongs to.',
    blocked: 'The Payments context is not built, so there are no transactions to show.',
  },
  payouts: {
    title: 'Payouts',
    icon: 'currency-circle-dollar',
    purpose: 'What each dealer is owed after commission, and the runs that paid them.',
    blocked:
      'Payouts need the Payments context, which is not built. Commission is computed and shown per booking at the rate each booking froze, but nothing schedules or records a payment to a dealer.',
    instead: {
      text: 'A dealer already sees their own revenue and commission at frozen rates on their reports screen.',
      label: 'Dealers',
      route: '/dealers',
    },
  },
  finance: {
    title: 'Finance',
    icon: 'chart-line-up',
    purpose:
      'Platform revenue: commission earned, deposits held, refunds issued, and what is owed out.',
    blocked:
      'Every figure on this screen would come from the Payments context, which is not built. Showing commission alone would read as money received, and none has been.',
  },
  reviews: {
    title: 'Reviews',
    icon: 'star',
    purpose:
      'Ratings customers leave for dealers and dealers leave for customers, and the moderation queue for them.',
    blocked:
      'The Review aggregate exists in the domain but has no table, no repository and no data. Until it does, a dealer with no reviews reads "No reviews yet" rather than showing a rating nobody gave.',
  },
  notifications: {
    title: 'Notifications',
    icon: 'bell',
    purpose: 'A feed of what needs an administrator: overdue reviews, breached SLAs, failed jobs.',
    blocked:
      'No notification feed has been built. Email is the only channel the platform sends on today.',
    instead: {
      text: 'The dashboard already ranks what needs attention, by the deadline each item froze.',
      label: 'Dashboard',
      route: '/dashboard',
    },
  },
};
