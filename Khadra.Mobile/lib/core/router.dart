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
import '../features/profile/reputation_screen.dart';
import '../features/profile/sessions_screen.dart';
import '../features/reviews/leave_review_screen.dart';
import '../features/reviews/reviews_screen.dart';
import '../features/shell/app_shell.dart';
import '../features/shortlist/shortlist_screen.dart';
import '../features/shell/splash_screen.dart';
import '../features/shell/welcome_screen.dart';
import 'providers.dart';
import 'session/session_controller.dart';

/// Route paths, named once so nothing types a URL twice.
abstract final class Routes {
  static const splash = '/';
  static const welcome = '/welcome';
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
  static const reputation = '/profile/reputation';
  static const shortlist = '/profile/saved';

  static String vehicle(String id) => '/vehicle/$id';
  static String gallery(String id) => '/gallery/$id';
  static String galleryReviews(String id) => '/gallery/$id/reviews';
  static String requestBooking(String vehicleId) => '/vehicle/$vehicleId/book';
  static String booking(String id) => '/bookings/$id';
  static String review(String bookingId) => '/bookings/$bookingId/review';
  static String openDispute(String bookingId) => '/bookings/$bookingId/dispute/new';
  static String dispute(String ticketId) => '/disputes/$ticketId';
}

/// [path] with the destination the customer was heading for attached, or the bare
/// path when there is none.
///
/// Five screens hand a destination on to a sixth — Get Started to both forms, the
/// account panel and sign-in to registration, sign-in and registration through
/// verification — and each of them wrote the same `Uri(...)` by hand. One of them
/// wrote it slightly differently, which is how `next` came to be dropped between
/// the panel and the form.
String routeWithNext(String path, String? next) => next == null
    ? path
    : Uri(path: path, queryParameters: {'next': next}).toString();

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
        Routes.reputation,
        Routes.shortlist,
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

      // The one place the Get Started gate is consulted: the app's own entry
      // point, at the moment of launch.
      //
      // NOT a global redirect, and that is the whole design. Public routes above
      // render before the session resolves precisely so a deep link survives a
      // cold start; a gate across every route would take that back, and opening
      // `/verify-email?token=…` from an email would land on a welcome screen with
      // the verification unconsumed. A launch decision is not a wall.
      //
      // One bypass, stated rather than discovered: on the WEB the browser's URL is
      // the initial location, so a typed or bookmarked `/search` never passes
      // through here. That is the browser's flow, and it is allowed to be.
      if (location == Routes.splash) {
        final next = state.uri.queryParameters['next'];

        // Signed in, or having already said how they want to use the app: straight
        // on. This is what makes a returning customer's launch land on Home.
        if (session.isSignedIn || ref.read(entryChoiceProvider)) {
          return next ?? Routes.search;
        }

        // A fresh install, app data cleared, or a sign-out. The destination travels
        // so a first launch from a link does not lose where it was going.
        return next == null
            ? Routes.welcome
            : Uri(path: Routes.welcome, queryParameters: {'next': next})
                .toString();
      }

      if (needsAccount && !session.isSignedIn) {
        // The destination travels so signing in lands where the tap was aimed,
        // rather than dumping the customer on the search screen.
        return Uri(path: Routes.signIn, queryParameters: {'next': location})
            .toString();
      }

      // Somebody already signed in has no business on the sign-in screen, or on the
      // one that offers to browse as a guest; deep links and the back button both
      // reach all three otherwise.
      if (session.isSignedIn &&
          (location == Routes.signIn ||
              location == Routes.register ||
              location == Routes.welcome)) {
        return Routes.search;
      }

      return null;
    },
    routes: [
      GoRoute(path: Routes.splash, builder: (_, __) => const SplashScreen()),
      GoRoute(
        path: Routes.welcome,
        builder: (_, state) =>
            WelcomeScreen(next: state.uri.queryParameters['next']),
      ),

      GoRoute(
        path: Routes.signIn,
        builder: (_, state) =>
            SignInScreen(next: state.uri.queryParameters['next']),
      ),
      GoRoute(
        path: Routes.register,
        builder: (_, state) =>
            RegisterScreen(next: state.uri.queryParameters['next']),
      ),
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
          // Registration sets this when the server accepted the account but
          // could NOT send the verification email. Dropping it here left the
          // screen telling somebody to watch an inbox nothing was sent to.
          undelivered: state.uri.queryParameters['undelivered'] == '1',
          next: state.uri.queryParameters['next'],
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
      GoRoute(
        path: Routes.reputation,
        builder: (_, __) => const ReputationScreen(),
      ),
      GoRoute(
        path: Routes.shortlist,
        builder: (_, __) => const ShortlistScreen(),
      ),
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
