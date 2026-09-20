import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:khadra_mobile/api/dtos.dart';
import 'package:khadra_mobile/core/providers.dart';
import 'package:khadra_mobile/core/router.dart';
import 'package:khadra_mobile/features/auth/account_required.dart';
import 'package:khadra_mobile/features/auth/sign_in_screen.dart';
import 'package:khadra_mobile/features/notifications/notification_providers.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:timezone/data/latest_all.dart' as tz_data;

import 'support/fake_api.dart';

/// What the three account tabs show a guest.
///
/// The owner's rule is that booking, saved cars, profile, bookings and alerts all
/// require an account. The tabs are NOT redirected to the sign-in form to enforce
/// it — a redirect leaves the tab shell, so the bottom bar disappears and the only
/// way back is the form's close button, and the Profile tab is where the language
/// switch lives, so gating it would leave an Arabic speaker who has not signed in
/// with no way out of English. The data is behind authentication in the two places
/// that matter instead: the providers never call the API without a session, and
/// every one of those endpoints is refused server-side regardless.
///
/// So these pin the panel that does the saying. It had drifted before it was shared:
/// Bookings and Alerts offered no way to CREATE an account, and the Alerts tab was
/// handed the bookings string and told people to sign in to see their bookings.
void main() {
  setUpAll(tz_data.initializeTimeZones);

  Future<GoRouter> pumpSignedOut(
    WidgetTester tester, {
    Locale locale = const Locale('en'),
    FakeApi? api,
  }) async {
    tester.view.physicalSize = const Size(1000, 2400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);

    SharedPreferences.setMockInitialValues({'khadra.entry_chosen': true});
    final preferences = await SharedPreferences.getInstance();

    final container = ProviderContainer(overrides: [
      apiProvider.overrideWithValue(api ?? FakeApi()),
      sessionStoreProvider.overrideWithValue(FakeSessionStore()),
      sharedPreferencesProvider.overrideWithValue(preferences),
      unreadNotificationCountProvider.overrideWith((ref) => Stream.value(0)),
    ]);
    addTearDown(container.dispose);

    await container.read(sessionProvider.notifier).restore();
    expect(container.read(sessionProvider).isSignedIn, isFalse);

    final router = container.read(routerProvider);

    await tester.pumpWidget(
      UncontrolledProviderScope(
        container: container,
        child: MaterialApp.router(
          locale: locale,
          routerConfig: router,
          supportedLocales: AppLocalizations.supportedLocales,
          localizationsDelegates: const [
            AppLocalizations.delegate,
            GlobalMaterialLocalizations.delegate,
            GlobalWidgetsLocalizations.delegate,
            GlobalCupertinoLocalizations.delegate,
          ],
        ),
      ),
    );
    await tester.pumpAndSettle();

    return router;
  }

  /// Every account tab, with the title it must carry and where signing in from it
  /// has to come back to.
  const tabs = <String, (String route, String title)>{
    'Bookings': (Routes.bookings, 'Sign in to see your bookings'),
    'Alerts': (Routes.notifications, 'Sign in to see your alerts'),
    'Profile': (Routes.profile, 'Sign in to your account'),
  };

  for (final MapEntry(key: name, value: (route, title)) in tabs.entries) {
    testWidgets('$name tells a guest it needs an account, and offers both ways in',
        (tester) async {
      final router = await pumpSignedOut(tester);

      router.go(route);
      await tester.pumpAndSettle();

      // The tab OPENS. It is not a redirect, so the bottom bar is still there and
      // the customer has not been thrown into a form for tapping a tab.
      expect(find.byType(NavigationBar), findsOneWidget);

      final panel = find.byType(AccountRequired);
      expect(panel, findsOneWidget);

      // Its own subject, not another screen's.
      expect(find.text(title), findsOneWidget);

      // Both ways in, which is what half of these were missing.
      expect(
        find.descendant(
            of: panel, matching: find.widgetWithText(FilledButton, 'Sign in')),
        findsOneWidget,
      );
      expect(
        find.descendant(
            of: panel,
            matching: find.widgetWithText(OutlinedButton, 'Create account')),
        findsOneWidget,
      );

      // And the tap is answered where it was made.
      expect(tester.widget<AccountRequired>(panel).next, route);

      await tester.tap(find.descendant(
          of: panel, matching: find.widgetWithText(FilledButton, 'Sign in')));
      await tester.pumpAndSettle();

      expect(find.byType(SignInScreen), findsOneWidget);
      expect(tester.widget<SignInScreen>(find.byType(SignInScreen)).next, route);
    });
  }

  testWidgets('the language switch stays reachable without an account',
      (tester) async {
    // The whole reason Profile is not gated. Somebody whose phone is in English and
    // who cannot read it must be able to change the app without signing in first.
    final router = await pumpSignedOut(tester);

    router.go(Routes.profile);
    await tester.pumpAndSettle();

    expect(find.byType(AccountRequired), findsOneWidget);
    expect(find.text('Language'.toUpperCase()), findsOneWidget);
    expect(find.text('العربية'), findsOneWidget);
    expect(find.text('Follow my device'), findsOneWidget);
  });

  testWidgets('no account tab fetches anything for a guest', (tester) async {
    // The requirement in the place it is actually enforced. "Requires
    // authentication" is not a panel saying so — it is the app never asking, and
    // the server refusing if it did. A signed-out visit must not produce a 401 a
    // second after the app opens.
    //
    // COUNTED, not inferred from the absence of an exception: `FakeApi` answers
    // all four of these endpoints, so a stray fetch would have been served
    // silently and the test would have passed on nothing.
    final api = _CountingApi();
    final router = await pumpSignedOut(tester, api: api);

    for (final (route, _) in tabs.values) {
      router.go(route);
      await tester.pumpAndSettle();
    }

    expect(api.calls, isEmpty);
    expect(tester.takeException(), isNull);
  });
}

/// Records every account-only endpoint a screen reaches for.
class _CountingApi extends FakeApi {
  final List<String> calls = <String>[];

  @override
  Future<Paged<BookingListItem>> myBookings({
    String? tab,
    int page = 1,
    int pageSize = 20,
  }) async {
    calls.add('myBookings');
    return super.myBookings(tab: tab, page: page, pageSize: pageSize);
  }

  @override
  Future<Map<String, int>> bookingTabCounts() async {
    calls.add('bookingTabCounts');
    return super.bookingTabCounts();
  }

  @override
  Future<NotificationFeed> notifications({int page = 1, int pageSize = 25}) async {
    calls.add('notifications');
    return super.notifications(page: page, pageSize: pageSize);
  }

  @override
  Future<int> unreadNotificationCount() async {
    calls.add('unreadNotificationCount');
    return super.unreadNotificationCount();
  }

  @override
  Future<CustomerDocuments> myDocuments() async {
    calls.add('myDocuments');
    return super.myDocuments();
  }

  @override
  Future<NextBooking?> nextBooking() async {
    calls.add('nextBooking');
    return super.nextBooking();
  }

  @override
  Future<AuthUser> me() async {
    calls.add('me');
    return super.me();
  }
}
