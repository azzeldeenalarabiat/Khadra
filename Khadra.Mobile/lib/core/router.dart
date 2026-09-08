import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../features/auth/forgot_password_screen.dart';
import '../features/auth/register_screen.dart';
import '../features/auth/reset_password_screen.dart';
import '../features/auth/sign_in_screen.dart';
import '../features/auth/verify_email_screen.dart';
import '../features/bookings/booking_detail_screen.dart';
import '../features/bookings/bookings_screen.dart';
import '../features/bookings/request_booking_screen.dart';
import '../features/catalogue/gallery_screen.dart';
import '../features/catalogue/search_screen.dart';
import '../features/catalogue/vehicle_screen.dart';
import '../features/disputes/dispute_screen.dart';
import '../features/disputes/open_dispute_screen.dart';
import '../features/documents/documents_screen.dart';
import '../features/notifications/notifications_screen.dart';
import '../features/profile/edit_profile_screen.dart';
import '../features/profile/change_password_screen.dart';
import '../features/profile/profile_screen.dart';
import '../features/profile/sessions_screen.dart';
import '../features/reviews/leave_review_screen.dart';
import '../features/reviews/reviews_screen.dart';
import '../features/shell/app_shell.dart';
import '../features/shell/splash_screen.dart';
import 'providers.dart';
import 'session/session_controller.dart';

/// Route paths, named once so nothing types a URL twice.
abstract final class Routes {
  static const splash = '/';
  static const search = '/search';
  static const bookings = '/bookings';
  static const notifications = '/notifications';
  static const profile = '/profile';

  static const signIn = '/sign-in';
  static const register = '/register';
  static const forgotPassword = '/forgot-password';
  static const resetPassword = '/reset-password';
  static const verifyEmail = '/verify-email';

  static const documents = '/profile/documents';
  static const editProfile = '/profile/edit';
  static const changePassword = '/profile/password';
  static const sessions = '/profile/sessions';

  static String vehicle(String id) => '/vehicle/$id';
  static String gallery(String id) => '/gallery/$id';
  static String galleryReviews(String id) => '/gallery/$id/reviews';
  static String requestBooking(String vehicleId) => '/vehicle/$vehicleId/book';
  static String booking(String id) => '/bookings/$id';
  static String review(String bookingId) => '/bookings/$bookingId/review';
  static String openDispute(String bookingId) => '/bookings/$bookingId/dispute/new';
  static String dispute(String ticketId) => '/disputes/$ticketId';
}

final routerProvider = Provider<GoRouter>((ref) {
  final notifier = _SessionRefreshNotifier(ref);
  ref.onDispose(notifier.dispose);

  return GoRouter(
    initialLocation: Routes.splash,
    refreshListenable: notifier,
    redirect: (context, state) {
      final session = ref.read(sessionProvider);
      final location = state.matchedLocation;

      // Which routes act on an ACCOUNT, as opposed to merely showing one.
      //
      // The four TABS are deliberately absent. Bookings, Alerts and Profile all
      // render their own signed-out state with a way in, which is gentler than
      // being thrown into a form — and the Profile tab is where the LANGUAGE
      // switch lives, so gating it would leave an Arabic speaker who has not
      // signed in with no way to change the app out of English.
      //
      // Browsing needs no account either: a tourist comparing prices before flying
      // to Jordan has no reason to create one first.
      const guarded = {
        Routes.documents,
        Routes.editProfile,
        Routes.changePassword,
        Routes.sessions,
      };

      final needsAccount = guarded.contains(location) ||
          location.startsWith('/bookings/') ||
          location.startsWith('/disputes/') ||
          location.endsWith('/book');

      // Only a GUARDED route has to wait for the cold-start rotation to answer.
      //
      // Making every route wait threw deep links away: opening
      // `/verify-email?token=…` from an email put the app on the splash, and by the
      // time the session resolved the destination was gone and the customer landed
      // on the search screen with their verification unconsumed. Public screens now
      // render immediately, and the few that need an account carry their
      // destination through the wait.
      if (!session.isResolved) {
        if (!needsAccount) return null;
        return location == Routes.splash
            ? null
            : Uri(path: Routes.splash, queryParameters: {'next': location})
                .toString();
      }

      if (location == Routes.splash) {
        return state.uri.queryParameters['next'] ?? Routes.search;
      }

      if (needsAccount && !session.isSignedIn) {
        // The destination travels so signing in lands where the tap was aimed,
        // rather than dumping the customer on the search screen.
        return Uri(path: Routes.signIn, queryParameters: {'next': location})
            .toString();
      }

      // Somebody already signed in has no business on the sign-in screen; deep
      // links and the back button both reach it otherwise.
      if (session.isSignedIn &&
          (location == Routes.signIn || location == Routes.register)) {
        return Routes.search;
      }

      return null;
    },
    routes: [
      GoRoute(path: Routes.splash, builder: (_, __) => const SplashScreen()),

      GoRoute(
        path: Routes.signIn,
        builder: (_, state) =>
            SignInScreen(next: state.uri.queryParameters['next']),
      ),
      GoRoute(path: Routes.register, builder: (_, __) => const RegisterScreen()),
      GoRoute(
        path: Routes.forgotPassword,
        builder: (_, __) => const ForgotPasswordScreen(),
      ),
      GoRoute(
        path: Routes.resetPassword,
        builder: (_, state) =>
            ResetPasswordScreen(token: state.uri.queryParameters['token']),
      ),
      GoRoute(
        path: Routes.verifyEmail,
        builder: (_, state) => VerifyEmailScreen(
          token: state.uri.queryParameters['token'],
          email: state.uri.queryParameters['email'],
        ),
      ),

      // The four tabs, sharing one shell so the bar does not rebuild between them.
      StatefulShellRoute.indexedStack(
        builder: (_, __, shell) => AppShell(shell: shell),
        branches: [
          StatefulShellBranch(routes: [
            GoRoute(path: Routes.search, builder: (_, __) => const SearchScreen()),
          ]),
          StatefulShellBranch(routes: [
            GoRoute(path: Routes.bookings, builder: (_, __) => const BookingsScreen()),
          ]),
          StatefulShellBranch(routes: [
            GoRoute(
              path: Routes.notifications,
              builder: (_, __) => const NotificationsScreen(),
            ),
          ]),
          StatefulShellBranch(routes: [
            GoRoute(path: Routes.profile, builder: (_, __) => const ProfileScreen()),
          ]),
        ],
      ),

      GoRoute(
        path: '/vehicle/:vehicleId',
        builder: (_, state) =>
            VehicleScreen(vehicleId: state.pathParameters['vehicleId']!),
        routes: [
          GoRoute(
            path: 'book',
            builder: (_, state) =>
                RequestBookingScreen(vehicleId: state.pathParameters['vehicleId']!),
          ),
        ],
      ),
      GoRoute(
        path: '/gallery/:dealerId',
        builder: (_, state) =>
            GalleryScreen(dealerId: state.pathParameters['dealerId']!),
        routes: [
          GoRoute(
            path: 'reviews',
            builder: (_, state) =>
                ReviewsScreen(dealerId: state.pathParameters['dealerId']!),
          ),
        ],
      ),
      GoRoute(
        path: '/bookings/:bookingId',
        builder: (_, state) =>
            BookingDetailScreen(bookingId: state.pathParameters['bookingId']!),
        routes: [
          GoRoute(
            path: 'review',
            builder: (_, state) =>
                LeaveReviewScreen(bookingId: state.pathParameters['bookingId']!),
          ),
          GoRoute(
            path: 'dispute/new',
            builder: (_, state) =>
                OpenDisputeScreen(bookingId: state.pathParameters['bookingId']!),
          ),
        ],
      ),
      GoRoute(
        path: '/disputes/:ticketId',
        builder: (_, state) =>
            DisputeScreen(ticketId: state.pathParameters['ticketId']!),
      ),

      GoRoute(path: Routes.documents, builder: (_, __) => const DocumentsScreen()),
      GoRoute(path: Routes.editProfile, builder: (_, __) => const EditProfileScreen()),
      GoRoute(
        path: Routes.changePassword,
        builder: (_, __) => const ChangePasswordScreen(),
      ),
      GoRoute(path: Routes.sessions, builder: (_, __) => const SessionsScreen()),
    ],
  );
});

/// Re-runs the redirect whenever the session changes.
///
/// Watching the whole state would rebuild the router on every profile edit; only
/// the STATUS decides where somebody may be, so only a change of status wakes it.
class _SessionRefreshNotifier extends ChangeNotifier {
  _SessionRefreshNotifier(Ref ref) {
    _subscription = ref.listen<SessionState>(
      sessionProvider,
      (previous, next) {
        if (previous?.status != next.status) notifyListeners();
      },
    );
  }

  late final ProviderSubscription<SessionState> _subscription;

  @override
  void dispose() {
    _subscription.close();
    super.dispose();
  }
}
