import { TranslationKey } from '../i18n/en';
import { NavGroup } from '../models/console.models';

/**
 * Sidebar structure, matching the AdminSidebar design component.
 *
 * Structure only. The two badges name a count the sidebar reads from GET /admin/workload;
 * no figure is written here.
 */
export const NAV_GROUPS: readonly NavGroup[] = [
  { items: [{ labelKey: 'nav.dashboard', icon: 'squares-four', route: '/dashboard' }] },
  {
    groupKey: 'nav.group.business',
    items: [
      { labelKey: 'nav.dealers', icon: 'storefront', route: '/dealers', count: 'dealers-pending' },
      { labelKey: 'nav.bookings', icon: 'calendar-check', route: '/bookings' },
      { labelKey: 'nav.customers', icon: 'users-three', route: '/customers' },
      { labelKey: 'nav.reviews', icon: 'star', route: '/reviews' },
    ],
  },
  {
    groupKey: 'nav.group.finance',
    items: [
      { labelKey: 'nav.finance', icon: 'chart-line-up', route: '/finance' },
      { labelKey: 'nav.payments', icon: 'credit-card', route: '/payments' },
      { labelKey: 'nav.payouts', icon: 'arrow-line-up-right', route: '/payouts' },
    ],
  },
  {
    groupKey: 'nav.group.operations',
    items: [
      { labelKey: 'nav.disputes', icon: 'scales', route: '/disputes', count: 'disputes-live' },
      { labelKey: 'nav.notifications', icon: 'bell', route: '/notifications' },
    ],
  },
  {
    groupKey: 'nav.group.platform',
    items: [
      { labelKey: 'nav.cities', icon: 'map-pin', route: '/cities' },
      { labelKey: 'nav.carTypes', icon: 'car-simple', route: '/car-types' },
      { labelKey: 'nav.settings', icon: 'sliders-horizontal', route: '/settings' },
    ],
  },
  {
    groupKey: 'nav.group.system',
    items: [
      { labelKey: 'nav.auditLogs', icon: 'list-magnifying-glass', route: '/audit-logs' },
      { labelKey: 'nav.adminUsers', icon: 'user-gear', route: '/admin-users' },
      { labelKey: 'nav.security', icon: 'shield-check', route: '/security' },
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
 * ONE list for both people who use it, not an owner's copy and an employee's copy. Two constants
 * cannot express Reports: an employee the owner has granted access must see that link and an
 * employee without the grant must not, and the difference is a per-person flag on the server, not a
 * role. So the items that are not everyone's name the permission they need (`requires`) and the
 * sidebar drops the ones this member of staff does not hold — the same discipline as `count`, which
 * names a figure without carrying one.
 *
 * Nothing here is gated on TRADING. A pending or suspended dealership keeps its whole rail; the gate
 * explains, once, why the screens behind it are locked. Hiding them would leave an owner wondering
 * what they had lost on top of wondering why.
 */
export const DEALER_NAV: readonly NavGroup[] = [
  {
    groupKey: 'nav.group.main',
    items: [{ labelKey: 'nav.dashboard', icon: 'squares-four', route: '/dealer/dashboard' }],
  },
  {
    groupKey: 'nav.group.operations',
    items: [
      { labelKey: 'nav.bookings', icon: 'calendar-check', route: '/dealer/bookings' },
      { labelKey: 'nav.fleet', icon: 'car-simple', route: '/dealer/fleet' },
    ],
  },
  {
    groupKey: 'nav.group.team',
    items: [
      {
        labelKey: 'nav.employees',
        icon: 'users-three',
        route: '/dealer/employees',
        requires: 'manage-staff',
      },
    ],
  },
  {
    groupKey: 'nav.group.business',
    items: [
      { labelKey: 'nav.dealerProfile', icon: 'storefront', route: '/dealer/profile' },
      { labelKey: 'nav.delivery', icon: 'moped', route: '/dealer/delivery' },
      { labelKey: 'nav.reviews', icon: 'star', route: '/dealer/reviews' },
    ],
  },
  {
    groupKey: 'nav.group.finance',
    items: [
      {
        labelKey: 'nav.reports',
        icon: 'chart-line-up',
        route: '/dealer/reports',
        requires: 'view-reports',
      },
    ],
  },
  {
    groupKey: 'nav.group.system',
    items: [
      { labelKey: 'nav.notifications', icon: 'bell', route: '/dealer/notifications' },
      { labelKey: 'nav.activity', icon: 'list-magnifying-glass', route: '/dealer/activity' },
      { labelKey: 'nav.settings', icon: 'sliders-horizontal', route: '/dealer/settings' },
    ],
  },
];

/**
 * What an EMPLOYEE sees (design: `Employee Console.dc.html`).
 *
 * Not the owner's rail with items removed — a shorter list arranged around a different job. An
 * employee's day is handovers: requests to answer, cars to give out, cars to take back. So Bookings
 * and Fleet are the Operations of it, the dealership itself is one read-only page rather than a
 * profile editor plus a delivery editor plus a staff screen, and there is no Team group at all
 * because staff are not theirs to manage.
 *
 * Reports is deliberately absent even for an employee who HOLDS the grant: the design gives them no
 * Finance group, and the figures they are allowed reach them through the dealer console. If the
 * owner wants a reports screen here later it is an addition, not a filter.
 */
export const EMPLOYEE_NAV: readonly NavGroup[] = [
  {
    groupKey: 'nav.group.main',
    items: [{ labelKey: 'nav.dashboard', icon: 'squares-four', route: '/employee/dashboard' }],
  },
  {
    groupKey: 'nav.group.operations',
    items: [
      { labelKey: 'nav.bookings', icon: 'calendar-check', route: '/employee/bookings' },
      { labelKey: 'nav.fleet', icon: 'car-simple', route: '/employee/fleet' },
    ],
  },
  {
    groupKey: 'nav.group.business',
    items: [{ labelKey: 'nav.myBusiness', icon: 'storefront', route: '/employee/business' }],
  },
  {
    groupKey: 'nav.group.system',
    items: [
      {
        labelKey: 'nav.notifications',
        icon: 'bell',
        route: '/employee/notifications',
        count: 'notifications-unread',
      },
      { labelKey: 'nav.settings', icon: 'sliders-horizontal', route: '/employee/settings' },
    ],
  },
];

/** Page title and breadcrumb parent for every route, keyed by route path. */
export const SCREEN_TITLES: Readonly<Record<string, TranslationKey>> = {
  dashboard: 'nav.dashboard',
  dealers: 'nav.dealers',
  // ':id' is what the topbar substitutes for a UUID segment, so one entry covers every record.
  'dealers/:id': 'screen.dealerApplication',
  bookings: 'nav.bookings',
  'bookings/:id': 'screen.bookingDetails',
  customers: 'nav.customers',
  'customers/:id': 'screen.customerProfile',
  finance: 'nav.finance',
  payments: 'nav.payments',
  'payments/detail': 'screen.paymentDetails',
  payouts: 'nav.payouts',
  disputes: 'nav.disputes',
  'disputes/:id': 'screen.disputeResolution',
  reviews: 'nav.reviews',
  cities: 'nav.cities',
  'car-types': 'nav.carTypes',
  settings: 'screen.platformSettings',
  notifications: 'nav.notifications',
  'audit-logs': 'screen.auditLogs',
  'admin-users': 'screen.adminUsers',
  security: 'nav.security',

  // The Dealer console (design: Dealer Console.dc.html, SCREENS).
  'dealer/apply': 'screen.submitYourGallery',
  'dealer/dashboard': 'nav.dashboard',
  'dealer/bookings': 'nav.bookings',
  'dealer/bookings/:id': 'screen.bookingDetails',
  'dealer/fleet': 'nav.fleet',
  'dealer/fleet/new': 'screen.addVehicle',
  'dealer/fleet/:id': 'screen.vehicleDetails',
  'dealer/fleet/:id/edit': 'screen.editVehicle',
  'dealer/employees': 'nav.employees',
  'dealer/profile': 'screen.dealerProfile',
  'dealer/profile/preview': 'screen.publicPreview',
  'dealer/delivery': 'nav.delivery',
  'dealer/reviews': 'nav.reviews',
  'dealer/reports': 'nav.reports',
  'dealer/notifications': 'nav.notifications',
  'dealer/activity': 'nav.activity',
  'dealer/settings': 'nav.settings',
  'dealer/disputes/:id': 'screen.dispute',

  // The Employee console (design: Employee Console.dc.html).
  'employee/dashboard': 'nav.dashboard',
  'employee/bookings': 'nav.bookings',
  'employee/bookings/:id': 'screen.bookingDetails',
  'employee/fleet': 'nav.fleet',
  'employee/fleet/:id': 'screen.vehicleDetails',
  'employee/business': 'screen.myBusiness',
  'employee/notifications': 'nav.notifications',
  'employee/settings': 'nav.settings',
  'employee/disputes/:id': 'screen.dispute',
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
  'employee/bookings/:id': 'employee/bookings',
  'employee/fleet/:id': 'employee/fleet',
  'employee/disputes/:id': 'employee/bookings',
};
