import { NavGroup } from '../models/console.models';

/**
 * Sidebar structure, matching the AdminSidebar design component.
 *
 * Structure only. The two badges name a count the sidebar reads from GET /admin/workload;
 * no figure is written here.
 */
export const NAV_GROUPS: readonly NavGroup[] = [
  { items: [{ label: 'Dashboard', icon: 'squares-four', route: '/dashboard' }] },
  {
    group: 'Business',
    items: [
      { label: 'Dealers', icon: 'storefront', route: '/dealers', count: 'dealers-pending' },
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
      { label: 'Disputes', icon: 'scales', route: '/disputes', count: 'disputes-live' },
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
    group: 'Main',
    items: [{ label: 'Dashboard', icon: 'squares-four', route: '/dealer/dashboard' }],
  },
  {
    group: 'Operations',
    items: [
      { label: 'Bookings', icon: 'calendar-check', route: '/dealer/bookings' },
      { label: 'Fleet', icon: 'car-simple', route: '/dealer/fleet' },
    ],
  },
  {
    group: 'Team',
    items: [{ label: 'Employees', icon: 'users-three', route: '/dealer/employees' }],
  },
  {
    group: 'Business',
    items: [
      { label: 'Dealer Profile', icon: 'storefront', route: '/dealer/profile' },
      { label: 'Delivery', icon: 'moped', route: '/dealer/delivery' },
      { label: 'Reviews', icon: 'star', route: '/dealer/reviews' },
    ],
  },
  {
    group: 'Finance',
    items: [{ label: 'Reports', icon: 'chart-line-up', route: '/dealer/reports' }],
  },
  {
    group: 'System',
    items: [
      { label: 'Notifications', icon: 'bell', route: '/dealer/notifications' },
      { label: 'Activity', icon: 'list-magnifying-glass', route: '/dealer/activity' },
      { label: 'Settings', icon: 'sliders-horizontal', route: '/dealer/settings' },
    ],
  },
];

/** Page title and breadcrumb parent for every route, keyed by route path. */
export const SCREEN_TITLES: Readonly<Record<string, string>> = {
  dashboard: 'Dashboard',
  dealers: 'Dealers',
  // ':id' is what the topbar substitutes for a UUID segment, so one entry covers every record.
  'dealers/:id': 'Dealer application',
  bookings: 'Bookings',
  'bookings/:id': 'Booking details',
  customers: 'Customers',
  'customers/:id': 'Customer profile',
  finance: 'Finance',
  payments: 'Payments',
  'payments/detail': 'Payment details',
  payouts: 'Payouts',
  disputes: 'Disputes',
  'disputes/:id': 'Dispute resolution',
  reviews: 'Reviews',
  cities: 'Cities & Regions',
  'car-types': 'Car Types',
  settings: 'Platform settings',
  notifications: 'Notifications',
  'audit-logs': 'Audit logs',
  'admin-users': 'Admin users',
  security: 'Security',

  // The Dealer console (design: Dealer Console.dc.html, SCREENS).
  'dealer/dashboard': 'Dashboard',
  'dealer/bookings': 'Bookings',
  'dealer/bookings/:id': 'Booking details',
  'dealer/fleet': 'Fleet',
  'dealer/fleet/new': 'Add vehicle',
  'dealer/fleet/:id': 'Vehicle details',
  'dealer/fleet/:id/edit': 'Edit vehicle',
  'dealer/employees': 'Employees',
  'dealer/profile': 'Dealer profile',
  'dealer/profile/preview': 'Public page preview',
  'dealer/delivery': 'Delivery',
  'dealer/reviews': 'Reviews',
  'dealer/reports': 'Reports',
  'dealer/notifications': 'Notifications',
  'dealer/activity': 'Activity',
  'dealer/settings': 'Settings',
  'dealer/disputes/:id': 'Dispute',
};

/** Detail screens sit under a list screen in the breadcrumb trail. */
export const SCREEN_PARENTS: Readonly<Record<string, string>> = {
  'dealers/:id': 'dealers',
  'bookings/:id': 'bookings',
  'customers/:id': 'customers',
  'payments/detail': 'payments',
  'disputes/:id': 'disputes',
  'dealer/bookings/:id': 'dealer/bookings',
  'dealer/fleet/new': 'dealer/fleet',
  'dealer/fleet/:id': 'dealer/fleet',
  'dealer/fleet/:id/edit': 'dealer/fleet',
  'dealer/profile/preview': 'dealer/profile',
  'dealer/disputes/:id': 'dealer/bookings',
};
