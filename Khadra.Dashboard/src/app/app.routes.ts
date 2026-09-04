import { Routes } from '@angular/router';
import { AdminShellComponent } from './layout/admin-shell.component';
import { adminSessionGuard } from './core/guards/admin-session.guard';
import { adminOnlyGuard, dealerStaffGuard } from './core/guards/role.guards';

/**
 * Console routes.
 *
 * A screen is here only if something real answers it. The Admin Console design draws several more —
 * customers, payments, payouts, finance, reviews, the lookups, the audit log, platform settings —
 * and those were built against sample content until that content was removed. They keep their route
 * and their place in the navigation, but they now say what they will show and what is missing,
 * rather than displaying figures nobody can act on. `notBuilt` names the entry that explains each.
 */
const notBuilt = (path: string, title: string, missing = path) => ({
  path,
  // The tab title is what a browser history entry and a bookmark are named by, so every route sets
  // one rather than leaving a dozen tabs all reading "Khadra".
  title: `${title} · Khadra Admin`,
  data: { missing },
  loadComponent: () =>
    import('./features/not-built/not-built.component').then((m) => m.NotBuiltComponent),
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
          // The dealer's own page as an administrator sees it is the application review screen:
          // same dealership, same documents, and the decisions attached. One screen, not two.
          { path: 'dealers/profile', pathMatch: 'full', redirectTo: 'dealers' },
          {
            path: 'dealers/:dealerId',
            title: 'Dealer application · Khadra Admin',
            loadComponent: () =>
              import('./features/dealers/dealer-review.component').then(
                (m) => m.DealerReviewComponent,
              ),
          },

          // Every booking on the platform, and the three interventions an admin can make in one.
          {
            path: 'bookings',
            title: 'Bookings · Khadra Admin',
            loadComponent: () =>
              import('./features/bookings/bookings-list.component').then(
                (m) => m.BookingsListComponent,
              ),
          },
          {
            path: 'bookings/:bookingId',
            title: 'Booking details · Khadra Admin',
            loadComponent: () =>
              import('./features/bookings/booking-detail.component').then(
                (m) => m.AdminBookingDetailComponent,
              ),
          },

          {
            path: 'customers',
            title: 'Customers · Khadra Admin',
            loadComponent: () =>
              import('./features/customers/customers-list.component').then(
                (m) => m.CustomersListComponent,
              ),
          },
          {
            path: 'customers/:customerId',
            title: 'Customer profile · Khadra Admin',
            loadComponent: () =>
              import('./features/customers/customer-profile.component').then(
                (m) => m.CustomerProfileComponent,
              ),
          },

          notBuilt('finance', 'Finance'),
          notBuilt('payments', 'Payments'),
          notBuilt('payments/detail', 'Payment details', 'payment-detail'),
          notBuilt('payouts', 'Payouts'),

          // Disputes are real: the queue, and the workspace where the platform's only decision
          // about money is made.
          {
            path: 'disputes',
            title: 'Disputes · Khadra Admin',
            loadComponent: () =>
              import('./features/disputes/disputes-list.component').then(
                (m) => m.DisputesListComponent,
              ),
          },
          {
            path: 'disputes/:ticketId',
            title: 'Dispute resolution · Khadra Admin',
            loadComponent: () =>
              import('./features/disputes/dispute-detail.component').then(
                (m) => m.DisputeDetailComponent,
              ),
          },

          notBuilt('reviews', 'Reviews'),
          notBuilt('cities', 'Cities & Regions'),
          notBuilt('car-types', 'Car Types', 'car-types'),
          // The entries have existed and been append-only from the start; only a way to read them
          // was missing.
          {
            path: 'audit-logs',
            title: 'Audit logs · Khadra Admin',
            loadComponent: () =>
              import('./features/audit/audit-log.component').then((m) => m.AuditLogComponent),
          },
          notBuilt('admin-users', 'Admin users', 'admin-users'),

          notBuilt('settings', 'Platform settings'),
          notBuilt('notifications', 'Notifications'),
          notBuilt('security', 'Security'),
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
              import('./features/fleet/vehicle-wizard.component').then(
                (m) => m.VehicleWizardComponent,
              ),
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
