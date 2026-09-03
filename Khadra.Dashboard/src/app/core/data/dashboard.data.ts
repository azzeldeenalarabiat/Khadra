import { IconName } from '../../shared/icon/icon-paths';
import { Tone } from '../models/console.models';

/**
 * Dashboard content from the Admin Console design.
 *
 * SAMPLE DATA. The console has no backend endpoints yet; these shapes are what
 * the API will need to return, so swapping in a real service means replacing the
 * constants, not the components.
 */

export interface KpiCard {
  readonly label: string;
  readonly icon: IconName;
  readonly main: string;
  readonly route: string;
  readonly subs: readonly { readonly k: string; readonly v: string; readonly tone?: Tone }[];
}

export const KPIS: readonly KpiCard[] = [
  {
    label: 'Total dealers',
    icon: 'storefront',
    main: '148',
    route: '/dealers',
    subs: [
      { k: 'Approved', v: '118' },
      { k: 'Pending review', v: '7', tone: 'warn' },
      { k: 'Suspended', v: '5', tone: 'bad' },
    ],
  },
  {
    label: 'Bookings',
    icon: 'calendar-check',
    main: '3,412',
    route: '/bookings',
    subs: [
      { k: 'Today', v: '96' },
      { k: 'Active', v: '214' },
      { k: 'Pending', v: '38', tone: 'warn' },
    ],
  },
  {
    label: 'Customers',
    icon: 'users-three',
    main: '8,240',
    route: '/customers',
    subs: [
      { k: 'Verified', v: '6,915' },
      { k: 'Pending verification', v: '412', tone: 'warn' },
      { k: 'Suspended', v: '31' },
    ],
  },
  {
    label: 'Revenue · JOD',
    icon: 'chart-line-up',
    main: '412,600',
    route: '/finance',
    subs: [
      { k: 'Commission', v: '82,520', tone: 'accent' },
      { k: 'Dealer payouts', v: '330,080' },
      { k: 'Refunds', v: '4,180' },
    ],
  },
  {
    label: 'Disputes',
    icon: 'scales',
    main: '12',
    route: '/disputes',
    subs: [
      { k: 'Pending admin', v: '5', tone: 'warn' },
      { k: 'Overdue', v: '2', tone: 'bad' },
      { k: 'Resolved 30d', v: '41' },
    ],
  },
];

export interface QueueItem {
  readonly severity: string;
  readonly tone: Tone;
  readonly title: string;
  readonly description: string;
  readonly entity: string;
  readonly sla: string;
  /** How much of the SLA window has elapsed, as a percentage. */
  readonly percent: number;
  readonly action: string;
  readonly route: string;
}

export const ATTENTION_QUEUE: readonly QueueItem[] = [
  {
    severity: 'Overdue',
    tone: 'bad',
    title: 'Dispute DSP-1184 is 61 hours old',
    description:
      'Customer claims the vehicle was returned undamaged; the dealer applied a JOD 180 penalty. Both parties submitted evidence.',
    entity: 'Aqaba Coast Cars · BK-20411',
    sla: '13h over',
    percent: 100,
    action: 'Resolve',
    route: '/disputes/detail',
  },
  {
    severity: 'Overdue',
    tone: 'bad',
    title: 'Dispute DSP-1190 is 54 hours old',
    description: 'No-show recorded after the 8-hour timeout. The customer disputes the deposit forfeit.',
    entity: 'Dead Sea Drive · BK-20455',
    sla: '6h over',
    percent: 100,
    action: 'Resolve',
    route: '/disputes/detail',
  },
  {
    severity: 'SLA 41h',
    tone: 'warn',
    title: '3 dealer applications are approaching the 48-hour SLA',
    description:
      'Wadi Rum Motors, Irbid Auto Lease and Jerash Rentals have complete document sets awaiting first review.',
    entity: 'Dealer approval queue',
    sla: '7h left',
    percent: 85,
    action: 'Review',
    route: '/dealers',
  },
  {
    severity: 'Payment',
    tone: 'bad',
    title: 'Payment PAY-99321 failed after three attempts',
    description: 'Deposit of JOD 120 was not captured. The booking is held and auto-cancels in 4 hours.',
    entity: 'Booking BK-20984',
    sla: '4h left',
    percent: 46,
    action: 'Open',
    route: '/payments/detail',
  },
  {
    severity: 'Dispute',
    tone: 'warn',
    title: 'New dispute: vehicle not delivered at pickup time',
    description: 'Customer waited 90 minutes at the Amman delivery address. The dealer has not responded.',
    entity: 'Al-Nadeem Rentals · DSP-1207',
    sla: '47h left',
    percent: 12,
    action: 'Assign',
    route: '/disputes/detail',
  },
  {
    severity: 'Payout',
    tone: 'dim',
    title: 'Payout batch of JOD 18,420 pending approval',
    description: '22 dealers, scheduled for 05 September. Two dealers have unresolved disputes on included bookings.',
    entity: 'Payout run PR-0091',
    sla: '2d left',
    percent: 20,
    action: 'Review',
    route: '/payouts',
  },
];

/** Fourteen daily booking counts, as a percentage of the tallest bar. */
export const BOOKING_TREND: readonly number[] = [38, 52, 44, 61, 49, 72, 58, 66, 47, 80, 69, 74, 62, 88];

export const MONEY_FLOW: readonly { readonly k: string; readonly v: string; readonly percent: number }[] = [
  { k: 'Gross booking value', v: '412,600', percent: 100 },
  { k: 'Platform commission', v: '82,520', percent: 20 },
  { k: 'Dealer payouts', v: '330,080', percent: 80 },
];

export const RECENT_ACTIVITY: readonly { readonly icon: IconName; readonly text: string; readonly ts: string }[] = [
  { icon: 'check-circle', text: 'Rania approved dealer Aqaba Coast Cars', ts: '9 min ago' },
  { icon: 'scales', text: 'Dispute DSP-1207 opened by customer', ts: '38 min ago' },
  { icon: 'credit-card', text: 'Payment PAY-99321 failed (3rd attempt)', ts: '1h ago' },
  { icon: 'user-gear', text: 'Omar suspended customer #882', ts: '2h ago' },
  { icon: 'sliders-horizontal', text: 'Commission rate changed 18% → 20%', ts: '5h ago' },
  { icon: 'arrow-line-up-right', text: 'Payout run PR-0090 paid — JOD 21,300', ts: 'Yesterday' },
  { icon: 'shield-check', text: 'New admin sign-in from Amman (2FA)', ts: 'Yesterday' },
];
