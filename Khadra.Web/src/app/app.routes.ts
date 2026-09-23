import { Routes } from '@angular/router';
import { languageGuard } from './core/routing/language.guard';
import { signedInGuard, signedOutGuard } from './core/session/auth.guards';
import { SiteShellComponent } from './layout/site-shell.component';

/**
 * Every page, once per language. The language is the first segment of every URL, so each language
 * version is its own indexable page and a shared link opens in the language it was shared in.
 *
 * `/` itself never reaches this table: `server.ts` answers it with a redirect chosen from the
 * browser's preference, before Angular runs.
 */
const pages: Routes = [
  { path: '', loadComponent: () => import('./features/home/home.component').then((m) => m.HomeComponent) },
  { path: 'cars', loadComponent: () => import('./features/cars/cars.component').then((m) => m.CarsComponent) },
  {
    path: 'cars/:slug',
    loadComponent: () => import('./features/car-details/car-details.component').then((m) => m.CarDetailsComponent),
  },
  { path: 'dealers', loadComponent: () => import('./features/dealers/dealers.component').then((m) => m.DealersComponent) },
  { path: 'dealers/:slug', loadComponent: () => import('./features/dealer/dealer.component').then((m) => m.DealerComponent) },

  // Signing in, and the pages an email links to. Rendered in the browser only; never indexed.
  {
    path: 'login',
    canActivate: [signedOutGuard],
    loadComponent: () => import('./features/auth/sign-in.component').then((m) => m.SignInComponent),
  },
  {
    path: 'register',
    canActivate: [signedOutGuard],
    loadComponent: () => import('./features/auth/register.component').then((m) => m.RegisterComponent),
  },
  { path: 'verify-email', loadComponent: () => import('./features/auth/verify-email.component').then((m) => m.VerifyEmailComponent) },
  {
    path: 'forgot-password',
    loadComponent: () => import('./features/auth/forgot-password.component').then((m) => m.ForgotPasswordComponent),
  },
  {
    path: 'reset-password',
    loadComponent: () => import('./features/auth/reset-password.component').then((m) => m.ResetPasswordComponent),
  },

  // The account. Every endpoint behind these refuses without a session; the guard only saves a detour.
  {
    path: 'profile',
    canActivate: [signedInGuard],
    loadComponent: () => import('./features/account/account-shell.component').then((m) => m.AccountShellComponent),
    children: [
      { path: '', loadComponent: () => import('./features/account/profile.component').then((m) => m.ProfileComponent) },
      { path: 'security', loadComponent: () => import('./features/account/security.component').then((m) => m.SecurityComponent) },
      { path: 'documents', loadComponent: () => import('./features/account/documents.component').then((m) => m.DocumentsComponent) },
    ],
  },
  {
    path: 'book/:vehicleId',
    canActivate: [signedInGuard],
    loadComponent: () => import('./features/book/book.component').then((m) => m.BookComponent),
  },
  {
    path: 'bookings',
    canActivate: [signedInGuard],
    loadComponent: () => import('./features/bookings/bookings.component').then((m) => m.BookingsComponent),
  },
  {
    path: 'bookings/:bookingId',
    canActivate: [signedInGuard],
    loadComponent: () => import('./features/bookings/booking-detail.component').then((m) => m.BookingDetailComponent),
  },
  {
    path: 'disputes/:ticketId',
    canActivate: [signedInGuard],
    loadComponent: () => import('./features/disputes/dispute.component').then((m) => m.DisputeComponent),
  },
  {
    path: 'saved',
    canActivate: [signedInGuard],
    loadComponent: () => import('./features/saved/saved.component').then((m) => m.SavedComponent),
  },
  {
    path: 'notifications',
    canActivate: [signedInGuard],
    loadComponent: () => import('./features/notifications/notifications.component').then((m) => m.NotificationsComponent),
  },
  { path: '**', loadComponent: () => import('./features/not-found/not-found.component').then((m) => m.NotFoundComponent) },
];

export const routes: Routes = [
  { path: 'ar', component: SiteShellComponent, canActivate: [languageGuard], data: { language: 'ar' }, children: pages },
  { path: 'en', component: SiteShellComponent, canActivate: [languageGuard], data: { language: 'en' }, children: pages },
  // A path with no language at all (an old or mistyped link): a real 404, in Arabic, at that address —
  // not a redirect to an error page, which a crawler would read as a page that moved.
  {
    path: '**',
    component: SiteShellComponent,
    canActivate: [languageGuard],
    data: { language: 'ar' },
    children: [
      { path: '**', loadComponent: () => import('./features/not-found/not-found.component').then((m) => m.NotFoundComponent) },
    ],
  },
];
