import { NavGroup } from '../models/console.models';

/** Sidebar structure, matching the AdminSidebar design component. */
export const NAV_GROUPS: readonly NavGroup[] = [
  { items: [{ label: 'Dashboard', icon: 'squares-four', route: '/dashboard' }] },
  {
    group: 'Business',
    items: [
      { label: 'Dealers', icon: 'storefront', route: '/dealers', badge: '7' },
      { label: 'Bookings', icon: 'calendar-check', route: '/bookings' },
      { label: 'Customers', icon: 'users-three', route: '/customers' },
      { label: 'Reviews', icon: 'star', route: '/reviews' },
    ],
  },
  {
    group: 'Finance',
    items: [
      { label: 'Finance', icon: 'chart-line-up', route: '/finance' },
      { label: 'Payments', icon: 'credit-card', route: '/payments' },
      { label: 'Payouts', icon: 'arrow-line-up-right', route: '/payouts' },
    ],
  },
  {
    group: 'Operations',
    items: [
      { label: 'Disputes', icon: 'scales', route: '/disputes', badge: '12' },
      { label: 'Notifications', icon: 'bell', route: '/notifications' },
    ],
  },
  {
    group: 'Platform',
    items: [
      { label: 'Cities & Regions', icon: 'map-pin', route: '/cities' },
      { label: 'Car Types', icon: 'car-simple', route: '/car-types' },
      { label: 'Settings', icon: 'sliders-horizontal', route: '/settings' },
    ],
  },
  {
    group: 'System',
    items: [
      { label: 'Audit Logs', icon: 'list-magnifying-glass', route: '/audit-logs' },
      { label: 'Admin Users', icon: 'user-gear', route: '/admin-users' },
      { label: 'Security', icon: 'shield-check', route: '/security' },
    ],
  },
];

/**
 * What a dealer sees.
 *
 * Spec 1.5 promises one dashboard serving Admin, Dealer Owner and Employee through role-based views,
 * so the navigation is CHOSEN by role rather than being one list with items hidden inside it. A
 * dealer has no business seeing a Payouts link they cannot open.
 *
 * Deliberately short: this is the first dealer-facing surface and only the fleet screens exist behind
 * it. It grows as their bookings, employees and reports land.
 */
export const DEALER_NAV: readonly NavGroup[] = [
  {
    group: 'Business',
    items: [{ label: 'My fleet', icon: 'car', route: '/fleet' }],
  },
];

/** Page title and breadcrumb parent for every route, keyed by route path. */
export const SCREEN_TITLES: Readonly<Record<string, string>> = {
  dashboard: 'Dashboard',
  dealers: 'Dealers',
  // ':id' is what the topbar substitutes for a UUID segment, so one entry covers every record.
  'dealers/:id': 'Dealer application',
  'dealers/profile': 'Dealer profile',
  bookings: 'Bookings',
  'bookings/detail': 'Booking details',
  customers: 'Customers',
  'customers/profile': 'Customer profile',
  finance: 'Finance',
  payments: 'Payments',
  'payments/detail': 'Payment details',
  payouts: 'Payouts',
  disputes: 'Disputes',
  'disputes/detail': 'Dispute resolution',
  reviews: 'Reviews',
  cities: 'Cities & Regions',
  'car-types': 'Car Types',
  settings: 'Platform settings',
  notifications: 'Notifications',
  'audit-logs': 'Audit logs',
  'admin-users': 'Admin users',
  security: 'Security',

  // Dealer-facing (spec 4.3).
  fleet: 'My fleet',
  'fleet/new': 'Add a car',
  'fleet/:id': 'Edit car',
};

/** Detail screens sit under a list screen in the breadcrumb trail. */
export const SCREEN_PARENTS: Readonly<Record<string, string>> = {
  'dealers/:id': 'dealers',
  'dealers/profile': 'dealers',
  'bookings/detail': 'bookings',
  'customers/profile': 'customers',
  'payments/detail': 'payments',
  'disputes/detail': 'disputes',
  'fleet/new': 'fleet',
  'fleet/:id': 'fleet',
};
