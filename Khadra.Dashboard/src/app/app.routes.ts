import { Routes } from '@angular/router';
import { AdminShellComponent } from './layout/admin-shell.component';

/**
 * Console routes.
 *
 * The eleven list screens share one lazily-loaded component and differ only by
 * the `list` key in their route data. Detail routes currently show a single
 * representative record, mirroring the design; they will take an `:id` once the
 * API exists.
 */
const list = (path: string, title: string) => ({
  path,
  // The tab title is what a browser history entry and a bookmark are named by,
  // so every route sets one rather than leaving eleven tabs all reading "Khadra".
  title: `${title} · Khadra Admin`,
  data: { list: path },
  loadComponent: () =>
    import('./features/list-screen/list-screen.component').then((m) => m.ListScreenComponent),
});

export const routes: Routes = [
  {
    path: '',
    component: AdminShellComponent,
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      {
        path: 'dashboard',
        title: 'Dashboard · Khadra Admin',
        loadComponent: () =>
          import('./features/dashboard/dashboard.component').then((m) => m.DashboardComponent),
      },

      list('dealers', 'Dealers'),
      {
        path: 'dealers/review',
        title: 'Dealer application · Khadra Admin',
        loadComponent: () =>
          import('./features/dealers/dealer-review.component').then((m) => m.DealerReviewComponent),
      },
      {
        path: 'dealers/profile',
        title: 'Dealer profile · Khadra Admin',
        loadComponent: () =>
          import('./features/dealers/dealer-profile.component').then(
            (m) => m.DealerProfileComponent,
          ),
      },

      list('bookings', 'Bookings'),
      {
        path: 'bookings/detail',
        title: 'Booking details · Khadra Admin',
        loadComponent: () =>
          import('./features/bookings/booking-detail.component').then(
            (m) => m.BookingDetailComponent,
          ),
      },

      list('customers', 'Customers'),
      {
        path: 'customers/profile',
        title: 'Customer profile · Khadra Admin',
        loadComponent: () =>
          import('./features/customers/customer-profile.component').then(
            (m) => m.CustomerProfileComponent,
          ),
      },

      {
        path: 'finance',
        title: 'Finance · Khadra Admin',
        loadComponent: () =>
          import('./features/finance/finance.component').then((m) => m.FinanceComponent),
      },

      list('payments', 'Payments'),
      {
        path: 'payments/detail',
        title: 'Payment details · Khadra Admin',
        loadComponent: () =>
          import('./features/payments/payment-detail.component').then(
            (m) => m.PaymentDetailComponent,
          ),
      },

      list('payouts', 'Payouts'),

      list('disputes', 'Disputes'),
      {
        path: 'disputes/detail',
        title: 'Dispute resolution · Khadra Admin',
        loadComponent: () =>
          import('./features/disputes/dispute-detail.component').then(
            (m) => m.DisputeDetailComponent,
          ),
      },

      list('reviews', 'Reviews'),
      list('cities', 'Cities & Regions'),
      list('car-types', 'Car Types'),
      list('audit-logs', 'Audit logs'),
      list('admin-users', 'Admin users'),

      {
        path: 'settings',
        title: 'Platform settings · Khadra Admin',
        loadComponent: () =>
          import('./features/settings/settings.component').then((m) => m.SettingsComponent),
      },
      {
        path: 'notifications',
        title: 'Notifications · Khadra Admin',
        loadComponent: () =>
          import('./features/notifications/notifications.component').then(
            (m) => m.NotificationsComponent,
          ),
      },
      {
        path: 'security',
        title: 'Security · Khadra Admin',
        loadComponent: () =>
          import('./features/security/security.component').then((m) => m.SecurityComponent),
      },

      { path: '**', redirectTo: 'dashboard' },
    ],
  },
];
