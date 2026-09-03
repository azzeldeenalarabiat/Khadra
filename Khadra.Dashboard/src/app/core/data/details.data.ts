import { IconName } from '../../shared/icon/icon-paths';
import { DocumentTile, KeyValue, TableConfig, TimelineStep, Tone } from '../models/console.models';

/**
 * Detail-screen content from the Admin Console design.
 *
 * SAMPLE DATA. Each detail screen shows one representative record, exactly as the
 * design does. Real routes will carry an :id and load the record through a service.
 */

/* ── Dealer application review ─────────────────────────────────────────── */

export const DEALER_APPLICATION = {
  initials: 'WM',
  name: 'Wadi Rum Motors',
  status: 'Pending Review',
  meta: 'Application DA-2291 · submitted 01 September 2026 08:14 · Wadi Rum',
  slaLeft: '7h 12m left',
  slaNote: '41h of 48h elapsed',
};

export const DEALER_BUSINESS_ROWS: readonly KeyValue[] = [
  { k: 'Business name', v: 'Wadi Rum Motors' },
  { k: 'Commercial registration', v: 'CR 91744 · issued 12 Mar 2024' },
  { k: 'Licence type', v: 'Licensed tourist car rental office' },
  { k: 'Location', v: 'Wadi Rum Village, main street — Aqaba Governorate' },
  { k: 'Contact', v: 'Faisal Zalabieh · +962 7 9012 4477 · office@wadirummotors.jo' },
  { k: 'Operating hours', v: 'Sat–Thu 08:00–20:00 · Fri 10:00–18:00' },
  { k: 'Fleet declared', v: '9 vehicles · 6 SUV, 2 Sedan, 1 Van' },
  { k: 'Delivery', v: 'Enabled · 45 km radius' },
];

export const DEALER_DOCUMENTS: readonly DocumentTile[] = [
  {
    label: 'Commercial registration',
    status: 'Verified',
    tone: 'ok',
    file: 'cr-91744.pdf · 1.2 MB',
    meta: 'Uploaded 01 Sep 08:14 · checked by Rania',
  },
  {
    label: 'Green-plate registration',
    status: 'Needs review',
    tone: 'warn',
    file: 'plates-scan.jpg · 3.4 MB',
    meta: 'Uploaded 01 Sep 08:16 · low resolution',
  },
  {
    label: 'Owner ID',
    status: 'Unavailable',
    tone: 'bad',
    file: 'owner-id.jpg · quarantined',
    meta: 'Failed virus scan 01 Sep 08:17',
  },
];

export const APPLICATION_TIMELINE: readonly TimelineStep[] = [
  { label: 'Application submitted', meta: '01 Sep 2026 08:14 · by the dealer', tone: 'ok' },
  { label: 'Under review', meta: '01 Sep 2026 09:02 · assigned to Rania Haddad', tone: 'ok' },
  { label: 'Clarification requested', meta: 'Not requested yet', tone: 'dim', future: true },
  {
    label: 'Approved / Rejected',
    meta: 'Due 03 Sep 2026 08:14 — 7h 12m left',
    tone: 'warn',
    future: true,
  },
];

/* ── Dealer profile ────────────────────────────────────────────────────── */

export const DEALER_PROFILE = {
  initials: 'AN',
  name: 'Al-Nadeem Rentals',
  status: 'Approved',
  meta: 'Amman · 4.8 ★ (312 reviews) · 24 cars · 412 bookings · CR 89168',
};

export const DEALER_TABS = [
  'Overview',
  'Documents',
  'Cars',
  'Bookings',
  'Employees',
  'Financial',
  'Reviews',
  'Activity',
] as const;

export const DEALER_PROFILE_ROWS: readonly KeyValue[] = [
  { k: 'Business name', v: 'Al-Nadeem Rentals' },
  { k: 'Commercial registration', v: 'CR 89168 · verified 14 Jan 2025' },
  { k: 'Location', v: 'Zahran Street, Amman' },
  { k: 'Contact', v: 'Nadeem Khoury · +962 7 7788 1120' },
  { k: 'Operating hours', v: 'Daily 07:00–23:00' },
  { k: 'Delivery', v: 'Enabled' },
  { k: 'Delivery radius', v: '30 km · JOD 8 fee' },
  { k: 'Account created', v: '12 January 2025' },
  { k: 'Employees', v: '6 · 2 can approve bookings' },
];

export const DEALER_VERIFICATION: readonly {
  label: string;
  meta: string;
  icon: IconName;
  tone: Tone;
}[] = [
  { label: 'Commercial registration', meta: 'Valid to 2027', icon: 'check-circle', tone: 'ok' },
  { label: 'Green-plate proof', meta: 'Valid to 2027', icon: 'check-circle', tone: 'ok' },
  { label: 'Owner ID', meta: 'Expires in 3 months', icon: 'warning-circle', tone: 'warn' },
];

export const DEALER_STATS: readonly KeyValue[] = [
  { k: 'Cars listed', v: '24' },
  { k: 'Bookings', v: '412' },
  { k: 'Rating', v: '4.8 ★' },
  { k: 'Open disputes', v: '2' },
];

const JOD = 'JOD';

/** Fixture figures are written the way the design showed them; strip the separator. */
const money = (value: string) => ({
  kind: 'money' as const,
  amount: Number(value.replace(/,/g, '')),
  currency: JOD,
});

/** Tables shown on the dealer profile's non-overview tabs. */
export const DEALER_TAB_TABLES: Readonly<Record<string, TableConfig>> = {
  Cars: {
    columns: ['Vehicle', 'Type', 'Year', 'Plate', 'Daily price', 'Status'],
    rows: [
      ['Toyota Corolla', 'Sedan', '2023', '42-19883', '310', 'Available', 'ok'],
      ['Toyota Land Cruiser', 'SUV', '2024', '42-20114', '980', 'Rented', 'accent'],
      ['Hyundai Accent', 'Sedan', '2022', '42-18770', '240', 'Available', 'ok'],
      ['Kia Carnival', 'Van', '2023', '42-19002', '520', 'Maintenance', 'warn'],
      ['Mercedes E200', 'Luxury', '2022', '42-17654', '1,050', 'Available', 'ok'],
    ].map((r) => ({
      id: r[0],
      cells: [
        { kind: 'text' as const, value: r[0] },
        { kind: 'text' as const, value: r[1] },
        { kind: 'text' as const, value: r[2] },
        { kind: 'text' as const, value: r[3] },
        money(r[4]),
        { kind: 'badge' as const, value: r[5], tone: r[6] as Tone },
      ],
    })),
  },
  Bookings: {
    columns: ['Booking', 'Customer', 'Vehicle', 'Dates', 'Total', 'Status'],
    rows: [
      ['BK-20984', 'Layla Odeh', 'Toyota Corolla', '04–08 Sep', '1,240', 'Pending', 'warn'],
      ['BK-20902', 'Nour Halabi', 'Land Cruiser', '20–27 Aug', '3,320', 'Completed', 'dim'],
      ['BK-20388', 'Nour Halabi', 'Land Cruiser', '24–31 Aug', '3,320', 'Cancelled', 'dim'],
      ['BK-20301', 'Omar Nasser', 'Hyundai Accent', '12–15 Aug', '720', 'Completed', 'dim'],
    ].map((r) => ({
      id: r[0],
      cells: [
        { kind: 'text' as const, value: r[0] },
        { kind: 'text' as const, value: r[1] },
        { kind: 'text' as const, value: r[2] },
        { kind: 'text' as const, value: r[3] },
        money(r[4]),
        { kind: 'badge' as const, value: r[5], tone: r[6] as Tone },
      ],
    })),
  },
  Employees: {
    columns: ['Name', 'Role', 'Can approve bookings', 'Last active'],
    rows: [
      ['John Sabbagh', 'Employee', 'Yes', 'Today 09:20'],
      ['Maha Odeh', 'Employee', 'Yes', 'Today 08:02'],
      ['Ziad Karam', 'Employee', 'No', 'Yesterday'],
      ['Nadeem Khoury', 'Owner', 'Yes', 'Today 07:40'],
    ].map((r) => ({ id: r[0], cells: r.map((v) => ({ kind: 'text' as const, value: v })) })),
  },
  Financial: {
    columns: ['Period', 'Bookings', 'Gross', 'Commission', 'Net payout', 'Status'],
    rows: [
      ['September 2026', '12', '8,410', '1,682', '6,728', 'Open', 'warn'],
      ['August 2026', '44', '31,220', '6,244', '24,976', 'Paid', 'ok'],
      ['July 2026', '39', '27,880', '5,018', '22,862', 'Paid', 'ok'],
    ].map((r) => ({
      id: r[0],
      cells: [
        { kind: 'text' as const, value: r[0] },
        { kind: 'text' as const, value: r[1] },
        money(r[2]),
        money(r[3]),
        money(r[4]),
        { kind: 'badge' as const, value: r[5], tone: r[6] as Tone },
      ],
    })),
  },
  Reviews: {
    columns: ['Reviewer', 'Rating', 'Comment', 'Booking', 'Status'],
    rows: [
      [
        'Layla Odeh',
        '4.8',
        'Car was clean and delivery was on time in Amman.',
        'BK-20902',
        'Published',
        'ok',
      ],
      ['Nour Halabi', '3.5', 'Pickup took longer than agreed.', 'BK-20388', 'Published', 'ok'],
      ['Omar Nasser', '5.0', 'Straightforward, good price.', 'BK-20301', 'Published', 'ok'],
    ].map((r) => ({
      id: r[0],
      cells: [
        { kind: 'text' as const, value: r[0] },
        { kind: 'text' as const, value: `${r[1]} ★` },
        { kind: 'text' as const, value: r[2], variant: 'clip-320' as const },
        { kind: 'text' as const, value: r[3] },
        { kind: 'badge' as const, value: r[4], tone: r[5] as Tone },
      ],
    })),
  },
  Activity: {
    columns: ['Timestamp', 'Actor', 'Action', 'Detail'],
    rows: [
      ['Today 09:20', 'John — Employee', 'Approved booking', 'BK-20984'],
      ['Today 07:40', 'Nadeem Khoury', 'Updated car price', 'Toyota Corolla · 290 → 310'],
      ['Yesterday 18:11', 'System', 'Dispute opened', 'DSP-1207'],
      ['01 Sep 14:05', 'Rania Haddad', 'Verified documents', 'Commercial registration'],
    ].map((r) => ({ id: r[0], cells: r.map((v) => ({ kind: 'text' as const, value: v })) })),
  },
};

/* ── Booking detail ────────────────────────────────────────────────────── */

export const BOOKING_DETAIL = {
  reference: 'Booking BK-20984',
  status: 'Pending',
  meta: 'Created 03 Sep 2026 09:12 · Rental 04–08 September 2026 · Amman',
  alert:
    'Deposit payment failed — this booking auto-cancels in 4 hours unless the deposit is captured.',
};

export const BOOKING_CARDS: readonly {
  title: string;
  icon: IconName;
  rows: readonly KeyValue[];
}[] = [
  {
    title: 'Booking summary',
    icon: 'receipt',
    rows: [
      { k: 'Booking ID', v: 'BK-20984' },
      { k: 'Status', v: 'Pending dealer approval' },
      { k: 'Created', v: '03 Sep 2026 09:12' },
      { k: 'Rental dates', v: '04 Sep — 08 Sep 2026' },
      { k: 'Pickup method', v: 'Delivery · Abdoun, Amman' },
      { k: 'Delivery fee', v: 'JOD 8' },
    ],
  },
  {
    title: 'Customer & dealer',
    icon: 'users-three',
    rows: [
      { k: 'Customer', v: 'Layla Odeh · Verified' },
      { k: 'Customer rating', v: '4.9 ★ (14 bookings)' },
      { k: 'Contact', v: '+962 7 9955 2010' },
      { k: 'Dealer', v: 'Al-Nadeem Rentals · Approved' },
      { k: 'Dealer rating', v: '4.8 ★' },
      { k: 'Approved by', v: 'John — Employee' },
    ],
  },
  {
    title: 'Payment',
    icon: 'credit-card',
    rows: [
      { k: 'Total booking value', v: 'JOD 1,240' },
      { k: 'Deposit paid', v: 'JOD 0 of 120' },
      { k: 'Remaining balance', v: 'JOD 1,240' },
      { k: 'Method / status', v: 'Visa 4242 · Failed' },
      { k: 'Commission (20%)', v: 'JOD 248' },
      { k: 'Dealer payout', v: 'JOD 992' },
    ],
  },
];

export const VEHICLE_ROWS: readonly KeyValue[] = [
  { k: 'Make / model', v: 'Toyota Corolla' },
  { k: 'Year', v: '2023' },
  { k: 'Plate', v: 'Green plate · 42-19883' },
  { k: 'Car type', v: 'Sedan' },
  { k: 'Daily price', v: 'JOD 310' },
  { k: 'Odometer at pickup', v: 'Not recorded yet' },
];

export const BOOKING_TIMELINE: readonly TimelineStep[] = [
  { label: 'Booking created', meta: '03 Sep 2026 09:12 · by the customer', tone: 'ok' },
  {
    label: 'Deposit payment attempted',
    meta: '03 Sep 2026 09:14 · failed, card declined',
    tone: 'bad',
  },
  { label: 'Booking approved', meta: '03 Sep 2026 09:20 · by John — Employee', tone: 'ok' },
  { label: 'Pickup confirmed', meta: 'Due 04 Sep 2026 10:00', tone: 'dim', future: true },
  { label: 'Return confirmed', meta: 'Due 08 Sep 2026 10:00', tone: 'dim', future: true },
  { label: 'Booking completed', meta: 'Pending', tone: 'dim', future: true },
];

/* ── Customer profile ──────────────────────────────────────────────────── */

export const CUSTOMER_PROFILE = {
  initials: 'LO',
  name: 'Layla Odeh',
  meta: 'Jordan · 4.9 ★ · 14 bookings · joined 04 February 2025',
};

export const CUSTOMER_TABS = [
  'Overview',
  'Bookings',
  'Reviews',
  'Verification',
  'Activity',
] as const;

export const CUSTOMER_ROWS: readonly KeyValue[] = [
  { k: 'Full name', v: 'Layla Odeh' },
  { k: 'Country', v: 'Jordan' },
  { k: 'Email', v: 'layla.odeh@example.com' },
  { k: 'Phone', v: '+962 7 9955 2010' },
  { k: 'Date of birth', v: '14 May 1994 · age 32' },
  { k: 'Verification', v: 'Verified 06 Feb 2025 by Omar Deeb' },
  { k: 'Account status', v: 'Active' },
  { k: 'Joined', v: '04 February 2025' },
];

export const CUSTOMER_STATS: readonly KeyValue[] = [
  { k: 'Total bookings', v: '14' },
  { k: 'Completed', v: '12' },
  { k: 'Cancellations', v: '1', tone: 'warn' },
  { k: 'No-shows', v: '0', tone: 'ok' },
];

export const CUSTOMER_DOCUMENTS: readonly DocumentTile[] = [
  {
    label: 'National ID',
    status: 'Verified',
    tone: 'ok',
    file: 'national-id.jpg · restricted',
    meta: 'Expires 12 Aug 2029',
  },
  {
    label: 'Driving licence',
    status: 'Verified',
    tone: 'ok',
    file: 'licence.jpg · restricted',
    meta: 'Expires 03 Mar 2027',
  },
  {
    label: 'Passport',
    status: 'Not required',
    tone: 'dim',
    file: 'not uploaded',
    meta: 'Required for foreign customers only',
  },
];

export const CUSTOMER_TAB_TABLES: Readonly<Record<string, TableConfig>> = {
  Bookings: {
    columns: ['Booking', 'Dealer', 'Vehicle', 'Dates', 'Total', 'Status'],
    rows: [
      ['BK-20984', 'Al-Nadeem Rentals', 'Toyota Corolla', '04–08 Sep', '1,240', 'Pending', 'warn'],
      ['BK-20790', 'Aqaba Coast Cars', 'Hyundai Tucson', '12–16 Aug', '980', 'Completed', 'dim'],
      ['BK-20612', 'Petra Wheels', 'Kia Rio', '02–05 Jul', '420', 'Completed', 'dim'],
      ['BK-20455', 'Dead Sea Drive', 'Kia Rio', '30 Aug–01 Sep', '420', 'Disputed', 'bad'],
    ].map((r) => ({
      id: r[0],
      cells: [
        { kind: 'text' as const, value: r[0] },
        { kind: 'text' as const, value: r[1] },
        { kind: 'text' as const, value: r[2] },
        { kind: 'text' as const, value: r[3] },
        money(r[4]),
        { kind: 'badge' as const, value: r[5], tone: r[6] as Tone },
      ],
    })),
  },
  Reviews: {
    columns: ['Dealer', 'Rating', 'Comment', 'Booking', 'Status'],
    rows: [
      [
        'Al-Nadeem Rentals',
        '4.8',
        'Car was clean and delivery was on time in Amman.',
        'BK-20902',
        'Published',
        'ok',
      ],
      [
        'Aqaba Coast Cars',
        '5.0',
        'Very smooth pickup at the airport.',
        'BK-20790',
        'Published',
        'ok',
      ],
    ].map((r) => ({
      id: r[0],
      cells: [
        { kind: 'text' as const, value: r[0] },
        { kind: 'text' as const, value: `${r[1]} ★` },
        { kind: 'text' as const, value: r[2], variant: 'clip-320' as const },
        { kind: 'text' as const, value: r[3] },
        { kind: 'badge' as const, value: r[4], tone: r[5] as Tone },
      ],
    })),
  },
  Activity: {
    columns: ['Timestamp', 'Action', 'Detail', 'Channel'],
    rows: [
      ['Today 09:12', 'Created booking', 'BK-20984', 'Mobile app'],
      ['Today 09:14', 'Payment failed', 'PAY-99321 · card declined', 'Mobile app'],
      ['12 Aug 10:02', 'Left review', 'Aqaba Coast Cars · 5.0', 'Web'],
      ['06 Feb 09:41', 'Verification approved', 'National ID + licence', 'Admin'],
    ].map((r) => ({ id: r[0], cells: r.map((v) => ({ kind: 'text' as const, value: v })) })),
  },
};

/* ── Payment detail ────────────────────────────────────────────────────── */

export const PAYMENT_DETAIL = {
  reference: 'Payment PAY-99321',
  status: 'Failed',
  meta: 'Deposit for BK-20984 · JOD 120 · Visa •••• 4242',
  failureTitle: 'Payment failed — card declined by the issuing bank',
  failureBody:
    'Three attempts, the last at 09:14. Processor code do_not_honor. No funds were captured and the customer was notified. The booking is held for 4 more hours.',
};

export const PAYMENT_ROWS: readonly KeyValue[] = [
  { k: 'Payment ID', v: 'PAY-99321' },
  { k: 'Booking', v: 'BK-20984' },
  { k: 'Customer', v: 'Layla Odeh' },
  { k: 'Dealer', v: 'Al-Nadeem Rentals' },
  { k: 'Amount', v: 'JOD 120 (deposit)' },
  { k: 'Method', v: 'Card · Visa •••• 4242' },
  { k: 'Transaction ID', v: 'txn_9f21ab' },
  { k: 'Processor', v: 'MEPS Gateway' },
  { k: 'Status', v: 'Failed — do_not_honor' },
];

export const PAYMENT_HISTORY: readonly TimelineStep[] = [
  { label: 'Payment initiated', meta: '03 Sep 2026 09:12 · JOD 120 · Visa 4242', tone: 'ok' },
  { label: 'Attempt 1 declined', meta: '09:12 · insufficient_funds', tone: 'bad' },
  { label: 'Attempt 2 declined', meta: '09:13 · do_not_honor', tone: 'bad' },
  { label: 'Attempt 3 declined', meta: '09:14 · do_not_honor · retries exhausted', tone: 'bad' },
  { label: 'Customer notified', meta: '09:14 · email and in-app', tone: 'ok' },
  {
    label: 'Booking hold expires',
    meta: '03 Sep 2026 13:14 · auto-cancel',
    tone: 'warn',
    future: true,
  },
];

/* ── Dispute detail ────────────────────────────────────────────────────── */

export const DISPUTE_DETAIL = {
  reference: 'Dispute DSP-1184',
  status: 'Overdue',
  priority: 'High priority',
  meta: 'Damage penalty disputed · opened by customer 27 Aug 2026 08:41 · BK-20411',
  slaOver: '13h 06m over',
  age: '61h',
};

export const DISPUTE_CARDS: readonly {
  title: string;
  icon: IconName;
  rows: readonly KeyValue[];
}[] = [
  {
    title: 'Booking',
    icon: 'receipt',
    rows: [
      { k: 'Booking', v: 'BK-20411' },
      { k: 'Rental', v: '27–30 Aug 2026' },
      { k: 'Value', v: 'JOD 360' },
      { k: 'Deposit', v: 'JOD 80' },
    ],
  },
  {
    title: 'Customer',
    icon: 'user',
    rows: [
      { k: 'Name', v: 'Jonas Weber' },
      { k: 'Country', v: 'Germany' },
      { k: 'Verification', v: 'Pending' },
      { k: 'Rating', v: '—' },
    ],
  },
  {
    title: 'Dealer',
    icon: 'storefront',
    rows: [
      { k: 'Business', v: 'Aqaba Coast Cars' },
      { k: 'Status', v: 'Approved' },
      { k: 'Rating', v: '4.9 ★' },
      { k: 'Past disputes', v: '1 resolved' },
    ],
  },
];

export const DISPUTE_EVIDENCE: readonly {
  label: string;
  by: string;
  tone: Tone;
  ts: string;
  file: string;
}[] = [
  {
    label: 'Front bumper at pickup',
    by: 'Dealer',
    tone: 'dim',
    ts: '27 Aug 09:04',
    file: 'pickup-front.jpg',
  },
  {
    label: 'Front bumper at return',
    by: 'Dealer',
    tone: 'dim',
    ts: '30 Aug 18:22',
    file: 'return-front.jpg',
  },
  {
    label: 'Customer photo at pickup',
    by: 'Customer',
    tone: 'accent',
    ts: '27 Aug 09:01',
    file: 'customer-pickup.jpg',
  },
  {
    label: 'Repair quote',
    by: 'Dealer',
    tone: 'dim',
    ts: '31 Aug 11:40',
    file: 'repair-quote.pdf',
  },
];

export const DISPUTE_TIMELINE: readonly TimelineStep[] = [
  {
    label: 'Dispute opened by customer',
    meta: '27 Aug 2026 08:41 · damage penalty disputed',
    tone: 'bad',
  },
  { label: 'Dealer submitted evidence', meta: '31 Aug 2026 11:40 · 3 files', tone: 'ok' },
  { label: 'Customer submitted evidence', meta: '31 Aug 2026 19:05 · 3 files', tone: 'ok' },
  { label: 'Under review', meta: '01 Sep 2026 09:10 · assigned to Rania Haddad', tone: 'ok' },
  { label: '48-hour SLA breached', meta: '02 Sep 2026 08:41 · escalated to High', tone: 'bad' },
  { label: 'Resolution', meta: 'Pending admin decision', tone: 'dim', future: true },
];

export const RESOLUTION_OPTIONS: readonly { label: string; desc: string }[] = [
  { label: 'No penalty', desc: 'Dealer claim rejected, deposit returned in full' },
  { label: 'Apply penalty', desc: 'Full JOD 180 charged to the customer' },
  { label: 'Partial penalty', desc: 'Split between dealer and customer' },
  { label: 'Refund deposit', desc: 'JOD 80 returned, no penalty recorded' },
];

export const PAST_RESOLUTIONS: readonly { res: string; reason: string; who: string; ts: string }[] =
  [
    {
      res: 'Partial penalty — JOD 45 to dealer',
      reason: 'Scratch visible in the pickup photo but not in the customer photo; cost shared.',
      who: 'Rania Haddad · Super Admin',
      ts: '12 Aug 2026 14:22',
    },
    {
      res: 'No penalty',
      reason: 'Fuel gauge photo supported the customer.',
      who: 'Omar Deeb · Support Admin',
      ts: '04 Jul 2026 10:08',
    },
  ];

/* ── Finance ───────────────────────────────────────────────────────────── */

export const FINANCE_KPIS: readonly { label: string; v: string; meta: string; tone?: Tone }[] = [
  { label: 'Gross revenue', v: '412,600', meta: '+12.4% vs August' },
  { label: 'Platform commission', v: '82,520', meta: '20% blended rate', tone: 'accent' },
  { label: 'Dealer payouts', v: '330,080', meta: '1,182 settled' },
  { label: 'Pending payouts', v: '18,420', meta: '22 dealers · 05 Sep run', tone: 'warn' },
  { label: 'Refunds', v: '4,180', meta: '31 refunds' },
  { label: 'Penalties', v: '2,640', meta: '18 applied', tone: 'warn' },
];

export const COMMISSION_ROWS: readonly {
  id: string;
  dealer: string;
  value: string;
  rate: string;
  amount: string;
  payout: string;
  status: string;
  tone: Tone;
  date: string;
}[] = [
  {
    id: 'BK-20984',
    dealer: 'Al-Nadeem Rentals',
    value: '1,240',
    rate: '20%',
    amount: '248',
    payout: '992',
    status: 'Paid',
    tone: 'ok',
    date: '01 Sep',
  },
  {
    id: 'BK-20977',
    dealer: 'Aqaba Coast Cars',
    value: '860',
    rate: '20%',
    amount: '172',
    payout: '688',
    status: 'Paid',
    tone: 'ok',
    date: '01 Sep',
  },
  {
    id: 'BK-20961',
    dealer: 'Petra Wheels',
    value: '2,105',
    rate: '18%',
    amount: '379',
    payout: '1,726',
    status: 'Processing',
    tone: 'warn',
    date: '02 Sep',
  },
  {
    id: 'BK-20940',
    dealer: 'Wadi Rum Motors',
    value: '640',
    rate: '20%',
    amount: '128',
    payout: '512',
    status: 'Pending',
    tone: 'dim',
    date: '02 Sep',
  },
  {
    id: 'BK-20933',
    dealer: 'Irbid Auto Lease',
    value: '1,480',
    rate: '15%',
    amount: '222',
    payout: '1,258',
    status: 'Pending',
    tone: 'dim',
    date: '02 Sep',
  },
  {
    id: 'BK-20918',
    dealer: 'Dead Sea Drive',
    value: '990',
    rate: '20%',
    amount: '198',
    payout: '792',
    status: 'Failed',
    tone: 'bad',
    date: '01 Sep',
  },
  {
    id: 'BK-20902',
    dealer: 'Al-Nadeem Rentals',
    value: '3,320',
    rate: '20%',
    amount: '664',
    payout: '2,656',
    status: 'Paid',
    tone: 'ok',
    date: '01 Sep',
  },
  {
    id: 'BK-20896',
    dealer: 'Jerash Rentals',
    value: '710',
    rate: '20%',
    amount: '142',
    payout: '568',
    status: 'Paid',
    tone: 'ok',
    date: '01 Sep',
  },
];

/* ── Platform settings ─────────────────────────────────────────────────── */

export interface SettingRow {
  readonly label: string;
  readonly desc: string;
  readonly value: string;
  readonly by: string;
  readonly when: string;
}

export const SETTINGS_GROUPS: readonly {
  title: string;
  icon: IconName;
  note: string;
  rows: readonly SettingRow[];
}[] = [
  {
    title: 'Booking rules',
    icon: 'calendar-check',
    note: 'Applied at booking creation',
    rows: [
      {
        label: 'Platform commission',
        desc: 'Share of each booking retained by the platform. Range 15–20%.',
        value: '20%',
        by: 'Rania Haddad',
        when: '02 Sep 2026 14:05',
      },
      {
        label: 'No-show timeout',
        desc: 'Hours after pickup time before a booking is marked no-show.',
        value: '8h',
        by: 'Rania Haddad',
        when: '18 Jul 2026 09:31',
      },
      {
        label: 'Cancellation grace period',
        desc: 'Free cancellation window after booking confirmation.',
        value: '24h',
        by: 'Omar Deeb',
        when: '02 May 2026 16:40',
      },
      {
        label: 'Default delivery fee',
        desc: 'Charged when a dealer delivers outside their free radius.',
        value: '8 JOD',
        by: 'Rania Haddad',
        when: '12 Mar 2026 11:02',
      },
    ],
  },
  {
    title: 'Penalty rules',
    icon: 'gavel',
    note: 'Used by dispute resolution',
    rows: [
      {
        label: 'Dealer non-delivery penalty',
        desc: 'Charged to the dealer when a confirmed vehicle is not delivered.',
        value: '50 JOD',
        by: 'Rania Haddad',
        when: '21 Jun 2026 13:55',
      },
      {
        label: 'Customer no-show penalty',
        desc: 'Share of the deposit forfeited on a no-show.',
        value: '100%',
        by: 'Yousef Barakat',
        when: '21 Jun 2026 13:58',
      },
      {
        label: 'Late return charge',
        desc: 'Extra day charged after this grace period.',
        value: '2h',
        by: 'Omar Deeb',
        when: '09 Feb 2026 08:12',
      },
    ],
  },
  {
    title: 'Verification rules',
    icon: 'identification-card',
    note: 'Applied at customer sign-up',
    rows: [
      {
        label: 'Minimum renter age',
        desc: 'Customers below this age cannot complete a booking.',
        value: '21',
        by: 'Rania Haddad',
        when: '04 Jan 2025 10:00',
      },
      {
        label: 'ID requirement',
        desc: 'National ID for residents, passport for foreign customers.',
        value: 'Required',
        by: 'Rania Haddad',
        when: '04 Jan 2025 10:00',
      },
      {
        label: 'Licence held for',
        desc: 'Minimum time since the driving licence was issued.',
        value: '2 years',
        by: 'Omar Deeb',
        when: '15 Apr 2026 12:20',
      },
      {
        label: 'Foreign customer deposit',
        desc: 'Additional deposit for customers without a Jordanian ID.',
        value: '150 JOD',
        by: 'Yousef Barakat',
        when: '15 Apr 2026 12:24',
      },
    ],
  },
];

/* ── Notifications ─────────────────────────────────────────────────────── */

export const NOTIFICATION_CATEGORIES: readonly { label: string; count: string; icon: IconName }[] =
  [
    { label: 'All', count: '9', icon: 'bell' },
    { label: 'Dealer approval', count: '3', icon: 'storefront' },
    { label: 'Dispute', count: '3', icon: 'scales' },
    { label: 'Payment', count: '1', icon: 'credit-card' },
    { label: 'Payout', count: '1', icon: 'arrow-line-up-right' },
    { label: 'Security', count: '1', icon: 'shield-check' },
    { label: 'System', count: '0', icon: 'gear-six' },
  ];

export const NOTIFICATIONS: readonly {
  title: string;
  desc: string;
  ts: string;
  icon: IconName;
  tone: Tone;
  unread: boolean;
  link: string;
  route: string;
}[] = [
  {
    title: 'Dealer application approaching SLA',
    desc: 'Wadi Rum Motors · 7h 12m left of the 48-hour window',
    ts: '12 min ago',
    icon: 'storefront',
    tone: 'warn',
    unread: true,
    link: 'Review',
    route: '/dealers/review',
  },
  {
    title: 'New dispute opened',
    desc: 'DSP-1207 · vehicle not delivered · Al-Nadeem Rentals',
    ts: '38 min ago',
    icon: 'scales',
    tone: 'warn',
    unread: true,
    link: 'Open',
    route: '/disputes/detail',
  },
  {
    title: 'Payment failed',
    desc: 'PAY-99321 · JOD 120 deposit · card declined',
    ts: '1h ago',
    icon: 'credit-card',
    tone: 'bad',
    unread: true,
    link: 'Open',
    route: '/payments/detail',
  },
  {
    title: 'Dispute breached the 48-hour SLA',
    desc: 'DSP-1184 · 13h over · escalated to High',
    ts: '2h ago',
    icon: 'scales',
    tone: 'bad',
    unread: true,
    link: 'Resolve',
    route: '/disputes/detail',
  },
  {
    title: 'Payout batch pending approval',
    desc: 'PR-0091 · JOD 18,420 across 22 dealers',
    ts: '5h ago',
    icon: 'arrow-line-up-right',
    tone: 'dim',
    unread: true,
    link: 'Review',
    route: '/payouts',
  },
  {
    title: 'Suspicious sign-in blocked',
    desc: 'omar@carrental.jo · 4 failed attempts from Bucharest',
    ts: '7h ago',
    icon: 'shield-check',
    tone: 'warn',
    unread: true,
    link: 'Review',
    route: '/security',
  },
  {
    title: 'Dealer approved',
    desc: 'Aqaba Coast Cars · approved by you',
    ts: 'Yesterday',
    icon: 'check-circle',
    tone: 'ok',
    unread: false,
    link: 'View',
    route: '/dealers/profile',
  },
  {
    title: 'Payout run completed',
    desc: 'PR-0090 · JOD 21,300 paid to 19 dealers',
    ts: 'Yesterday',
    icon: 'arrow-line-up-right',
    tone: 'ok',
    unread: false,
    link: 'View',
    route: '/payouts',
  },
];

/* ── Security ──────────────────────────────────────────────────────────── */

export const SESSIONS: readonly {
  device: string;
  meta: string;
  icon: IconName;
  current: boolean;
  revocable: boolean;
}[] = [
  {
    device: 'MacBook Pro · Chrome 128',
    meta: 'Amman, Jordan · 94.187.x.x · active now',
    icon: 'laptop',
    current: true,
    revocable: false,
  },
  {
    device: 'iPhone 15 · Safari',
    meta: 'Amman, Jordan · last active 2h ago',
    icon: 'device-mobile',
    current: false,
    revocable: true,
  },
  {
    device: 'Windows 11 · Edge',
    meta: 'Irbid, Jordan · last active 3d ago',
    icon: 'desktop',
    current: false,
    revocable: true,
  },
  {
    device: 'iPad · Safari',
    meta: 'Aqaba, Jordan · last active 11d ago',
    icon: 'device-tablet',
    current: false,
    revocable: true,
  },
];

export const LOGIN_HISTORY: readonly { who: string; meta: string; result: string; tone: Tone }[] = [
  {
    who: 'Rania Haddad · Super Admin',
    meta: 'Today 08:52 · Amman · 2FA',
    result: 'Success',
    tone: 'ok',
  },
  {
    who: 'Omar Deeb · Support Admin',
    meta: 'Today 02:14 · Bucharest · 4 attempts',
    result: 'Blocked',
    tone: 'bad',
  },
  {
    who: 'Yousef Barakat · Finance Admin',
    meta: 'Yesterday 17:40 · Amman · 2FA',
    result: 'Success',
    tone: 'ok',
  },
  {
    who: 'Nadia Sweiss · Support Admin',
    meta: '01 Sep 09:03 · Irbid · 2FA',
    result: 'Success',
    tone: 'ok',
  },
  {
    who: 'Khalid Amr · Finance Admin',
    meta: '31 Aug 22:10 · Amman · no 2FA enrolled',
    result: 'Warning',
    tone: 'warn',
  },
  {
    who: 'Dana Qasem · disabled account',
    meta: '28 Aug 11:02 · Amman',
    result: 'Denied',
    tone: 'bad',
  },
];
