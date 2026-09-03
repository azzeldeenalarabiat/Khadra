import { Routes } from '@angular/router';
import { AdminShellComponent } from './layout/admin-shell.component';
import { adminSessionGuard } from './core/guards/admin-session.guard';
import { adminOnlyGuard, dealerStaffGuard } from './core/guards/role.guards';

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
  // The auth screens sit OUTSIDE the admin shell: no sidebar, no topbar, and no session required to
  // reach them. The reset path matches the link AuthEmailComposer builds ({base}/reset-password?token=).
  {
    path: 'sign-in',
    title: 'Sign in · Khadra Admin',
    loadComponent: () => import('./features/auth/sign-in.component').then((m) => m.SignInComponent),
  },
  {
    path: 'forgot-password',
    title: 'Reset your password · Khadra Admin',
    loadComponent: () =>
      import('./features/auth/forgot-password.component').then((m) => m.ForgotPasswordComponent),
  },
  {
    path: 'reset-password',
    title: 'Choose a new password · Khadra Admin',
    loadComponent: () =>
      import('./features/auth/reset-password.component').then((m) => m.ResetPasswordComponent),
  },
  // The link an invited employee receives (spec 4.2): {base}/accept-invitation?token=
  {
    path: 'accept-invitation',
    title: 'Accept your invitation · Khadra',
    loadComponent: () =>
      import('./features/auth/accept-invitation.component').then(
        (m) => m.AcceptInvitationComponent,
      ),
  },
  {
    path: '',
    component: AdminShellComponent,
    canActivate: [adminSessionGuard],
    children: [
      // Static redirect on purpose: redirectTo is evaluated while the URL is matched, before any
      // guard has resolved the session, so it cannot know the role yet. The role bounce happens a
      // moment later in adminOnlyGuard, once the session is actually loaded.
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },

      // Every platform screen in one guarded group. Guarding the section rather than each route
      // means a screen added later is admin-only by default instead of by remembering.
      {
        path: '',
        canActivateChild: [adminOnlyGuard],
        children: [
          {
            path: 'dashboard',
            title: 'Dashboard · Khadra Admin',
            loadComponent: () =>
              import('./features/dashboard/dashboard.component').then((m) => m.DashboardComponent),
          },

          {
            path: 'dealers',
            title: 'Dealers · Khadra Admin',
            loadComponent: () =>
              import('./features/dealers/dealers-list.component').then(
                (m) => m.DealersListComponent,
              ),
          },
          {
            path: 'dealers/profile',
            title: 'Dealer profile · Khadra Admin',
            loadComponent: () =>
              import('./features/dealers/dealer-profile.component').then(
                (m) => m.DealerProfileComponent,
              ),
          },
          // Literal segments first: this one would otherwise swallow /dealers/profile.
          {
            path: 'dealers/:dealerId',
            title: 'Dealer application · Khadra Admin',
            loadComponent: () =>
              import('./features/dealers/dealer-review.component').then(
                (m) => m.DealerReviewComponent,
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
        ],
      },

      // ── The Dealer console (design: Dealer Console.dc.html). One guarded group under /dealer, so
      // its paths can never collide with the Admin's (/bookings is the platform's list; /dealer/bookings
      // is one dealership's) and a screen added later is dealer-only by default.
      {
        path: 'dealer',
        canActivateChild: [dealerStaffGuard],
        // The gate: a dealership that cannot trade sees why, instead of a console of 403s.
        loadComponent: () =>
          import('./features/dealer/dealer-gate.component').then((m) => m.DealerGateComponent),
        children: [
          { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
          {
            path: 'dashboard',
            title: 'Dashboard · Khadra',
            loadComponent: () =>
              import('./features/dealer/dealer-dashboard.component').then(
                (m) => m.DealerDashboardComponent,
              ),
          },
          {
            path: 'bookings',
            title: 'Bookings · Khadra',
            loadComponent: () =>
              import('./features/dealer/dealer-bookings.component').then(
                (m) => m.DealerBookingsComponent,
              ),
          },
          {
            path: 'bookings/:bookingId',
            title: 'Booking details · Khadra',
            loadComponent: () =>
              import('./features/dealer/booking-detail.component').then(
                (m) => m.DealerBookingDetailComponent,
              ),
          },
          {
            path: 'disputes/:ticketId',
            title: 'Dispute · Khadra',
            loadComponent: () =>
              import('./features/dealer/dealer-dispute.component').then(
                (m) => m.DealerDisputeComponent,
              ),
          },
          {
            path: 'employees',
            title: 'Employees · Khadra',
            loadComponent: () =>
              import('./features/dealer/dealer-employees.component').then(
                (m) => m.DealerEmployeesComponent,
              ),
          },
          {
            path: 'delivery',
            title: 'Delivery · Khadra',
            loadComponent: () =>
              import('./features/dealer/dealer-delivery.component').then(
                (m) => m.DealerDeliveryComponent,
              ),
          },
          {
            path: 'reviews',
            title: 'Reviews · Khadra',
            data: { kind: 'reviews' },
            loadComponent: () =>
              import('./features/dealer/not-live.component').then((m) => m.NotLiveComponent),
          },
          {
            path: 'notifications',
            title: 'Notifications · Khadra',
            data: { kind: 'notifications' },
            loadComponent: () =>
              import('./features/dealer/not-live.component').then((m) => m.NotLiveComponent),
          },
          {
            path: 'profile',
            title: 'Dealer profile · Khadra',
            loadComponent: () =>
              import('./features/dealer/dealer-profile.component').then(
                (m) => m.DealerProfileComponent,
              ),
          },
          {
            path: 'reports',
            title: 'Reports · Khadra',
            loadComponent: () =>
              import('./features/dealer/dealer-reports.component').then(
                (m) => m.DealerReportsComponent,
              ),
          },
          {
            path: 'activity',
            title: 'Activity · Khadra',
            loadComponent: () =>
              import('./features/dealer/dealer-activity.component').then(
                (m) => m.DealerActivityComponent,
              ),
          },
          {
            path: 'settings',
            title: 'Settings · Khadra',
            loadComponent: () =>
              import('./features/dealer/dealer-settings.component').then(
                (m) => m.DealerSettingsComponent,
              ),
          },
          {
            path: 'fleet',
            title: 'Fleet · Khadra',
            loadComponent: () =>
              import('./features/fleet/fleet-list.component').then((m) => m.FleetListComponent),
          },
          {
            path: 'fleet/new',
            title: 'Add vehicle · Khadra',
            loadComponent: () =>
              import('./features/fleet/vehicle-wizard.component').then((m) => m.VehicleWizardComponent),
          },
          {
            path: 'fleet/:vehicleId',
            title: 'Vehicle · Khadra',
            loadComponent: () =>
              import('./features/fleet/vehicle-detail.component').then(
                (m) => m.VehicleDetailComponent,
              ),
          },
          {
            path: 'fleet/:vehicleId/edit',
            title: 'Edit vehicle · Khadra',
            loadComponent: () =>
              import('./features/fleet/car-form.component').then((m) => m.CarFormComponent),
          },
        ],
      },

      // Anything unrecognised goes to whichever home the signed-in role has.
      { path: '**', redirectTo: 'dashboard' },
    ],
  },
];
