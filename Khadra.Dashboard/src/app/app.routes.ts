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
const list = (path: string, key: string) => ({
  path,
  data: { list: key },
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
        loadComponent: () => import('./features/dashboard/dashboard.component').then((m) => m.DashboardComponent),
      },

      list('dealers', 'dealers'),
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
          import('./features/dealers/dealer-profile.component').then((m) => m.DealerProfileComponent),
      },

      list('bookings', 'bookings'),
      {
        path: 'bookings/detail',
        title: 'Booking details · Khadra Admin',
        loadComponent: () =>
          import('./features/bookings/booking-detail.component').then((m) => m.BookingDetailComponent),
      },

      list('customers', 'customers'),
      {
        path: 'customers/profile',
        title: 'Customer profile · Khadra Admin',
        loadComponent: () =>
          import('./features/customers/customer-profile.component').then((m) => m.CustomerProfileComponent),
      },

      {
        path: 'finance',
        title: 'Finance · Khadra Admin',
        loadComponent: () => import('./features/finance/finance.component').then((m) => m.FinanceComponent),
      },

      list('payments', 'payments'),
      {
        path: 'payments/detail',
        title: 'Payment details · Khadra Admin',
        loadComponent: () =>
          import('./features/payments/payment-detail.component').then((m) => m.PaymentDetailComponent),
      },

      list('payouts', 'payouts'),

      list('disputes', 'disputes'),
      {
        path: 'disputes/detail',
        title: 'Dispute resolution · Khadra Admin',
        loadComponent: () =>
          import('./features/disputes/dispute-detail.component').then((m) => m.DisputeDetailComponent),
      },

      list('reviews', 'reviews'),
      list('cities', 'cities'),
      list('car-types', 'car-types'),
      list('audit-logs', 'audit-logs'),
      list('admin-users', 'admin-users'),

      {
        path: 'settings',
        title: 'Platform settings · Khadra Admin',
        loadComponent: () => import('./features/settings/settings.component').then((m) => m.SettingsComponent),
      },
      {
        path: 'notifications',
        title: 'Notifications · Khadra Admin',
        loadComponent: () =>
          import('./features/notifications/notifications.component').then((m) => m.NotificationsComponent),
      },
      {
        path: 'security',
        title: 'Security · Khadra Admin',
        loadComponent: () => import('./features/security/security.component').then((m) => m.SecurityComponent),
      },

      { path: '**', redirectTo: 'dashboard' },
    ],
  },
];
