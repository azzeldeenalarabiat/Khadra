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
  bookings: {
    title: 'Bookings',
    icon: 'calendar-blank',
    purpose:
      'Every booking on the platform, across all dealers, with the frozen price and terms each one was made under.',
    blocked:
      'The Booking context is built and bookings are real, but `GET /api/v1/bookings` answers only for the customer or the dealer on a booking. A platform-wide reader for administrators has not been written.',
    instead: {
      text: 'A dealer sees their own bookings today, and a dispute opens the whole booking behind it.',
      label: 'Open the dispute queue',
      route: '/disputes',
    },
  },
  'booking-detail': {
    title: 'Booking details',
    icon: 'calendar-blank',
    purpose:
      'One booking as the platform sees it: the frozen pricing and terms, the handover records, and every status change with the person who made it.',
    blocked:
      'There is no administrator-facing booking endpoint yet. The same booking is already visible in full from the dispute raised against it.',
    instead: {
      text: 'Open the ticket on a booking to see the whole booking with it.',
      label: 'Open the dispute queue',
      route: '/disputes',
    },
  },
  customers: {
    title: 'Customers',
    icon: 'user',
    purpose:
      'The people who rent: their verification state, their documents, and the bookings behind their reputation.',
    blocked:
      'Customers exist as users and can upload their own documents, but no administrator endpoints for listing or reviewing them have been built.',
  },
  'customer-profile': {
    title: 'Customer profile',
    icon: 'user',
    purpose:
      'One customer: identity documents, booking history, disputes raised, and the account actions an administrator can take.',
    blocked: 'No administrator endpoint exists for reading or acting on a customer account.',
  },
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
  cities: {
    title: 'Cities & regions',
    icon: 'map-pin',
    purpose: 'The places customers filter by, and the regions delivery is judged against.',
    blocked:
      'The city lookup has not been built. Dealer and delivery locations are coordinates today, and distance is measured directly from them.',
  },
  'car-types': {
    title: 'Car types',
    icon: 'car-simple',
    purpose: 'The vehicle categories customers browse by: sedan, SUV, van and the rest.',
    blocked:
      'The car-type lookup has not been built. Every vehicle currently points at a single placeholder type id, which becomes a real row when the table ships.',
  },

  'admin-users': {
    title: 'Admin users',
    icon: 'user',
    purpose: 'Who can administer the platform, and what each of them may do.',
    blocked:
      'Administrator accounts exist and are seeded, but there are no endpoints to invite, list or change one, and no permission model finer than the Admin role.',
  },
  settings: {
    title: 'Platform settings',
    icon: 'gear',
    purpose:
      'The numbers the whole platform runs on: commission, deposit, cancellation window, no-show timeout, delivery fee, penalty tiers and the review SLA.',
    blocked:
      'These values are real and enforced, but they come from configuration through `IBusinessRulesProvider`, not from an editable record. The `BusinessRuleSettings` aggregate is designed for exactly this and is not yet wired to a table or an endpoint.',
    instead: {
      text: 'A booking freezes the numbers it was made under, so editing them later can never rewrite a past booking.',
      label: 'Dispute queue',
      route: '/disputes',
    },
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
  security: {
    title: 'Security',
    icon: 'lock-simple',
    purpose:
      'Active sessions, sign-in history and the controls to end a session or require a second factor.',
    blocked:
      'Refresh tokens do record the device, address and time behind every session, and a password change or suspension already revokes them all. There is no endpoint to read that history, and no second factor.',
  },
};
