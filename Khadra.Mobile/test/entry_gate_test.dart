import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:khadra_mobile/core/providers.dart';
import 'package:khadra_mobile/core/router.dart';
import 'package:khadra_mobile/features/auth/sign_in_screen.dart';
import 'package:khadra_mobile/features/auth/verify_email_screen.dart';
import 'package:khadra_mobile/features/shell/welcome_screen.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:timezone/data/latest_all.dart' as tz_data;

import 'support/fake_api.dart';

/// Where a launch lands, and why.
///
/// Four launches have to be told apart and the app used to make no distinction at
/// all — it opened on the catalogue every time, so a first-time customer was never
/// told an account existed, and somebody who had just signed out was left browsing
/// as though nothing had happened:
///
/// - a fresh install, or one whose app data was cleared, where nobody has said how
///   they want to use the app;
/// - a guest who has said so, and should not be asked twice;
/// - a returning customer with a session on disk, who should see Home;
/// - any of the above arriving from a LINK, whose destination must survive.
///
/// These drive the REAL router, because the decision lives in its redirect and a
/// stub would be testing a route table this app does not have.
void main() {
  setUpAll(tz_data.initializeTimeZones);

  /// The preferences key the entry choice is remembered under. Named here so the
  /// test asserts on what is actually persisted rather than on the getter that
  /// reads it — a flag only kept in memory would pass the one and fail the other.
  const chosenKey = 'khadra.entry_chosen';

  Uri at(GoRouter router) => router.routerDelegate.currentConfiguration.uri;

  String where(GoRouter router) => at(router).path;

  Future<(GoRouter, ProviderContainer, SharedPreferences, FakeApi)> launch(
    WidgetTester tester, {
    bool chosen = false,
    bool signedIn = false,
    Locale locale = const Locale('en'),
  }) async {
    // Tall enough for the three buttons and the language row to lay out.
    tester.view.physicalSize = const Size(1000, 2000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);

    SharedPreferences.setMockInitialValues(chosen ? {chosenKey: true} : {});
    final preferences = await SharedPreferences.getInstance();

    final api = FakeApi();
    final container = ProviderContainer(overrides: [
      apiProvider.overrideWithValue(api),
      sessionStoreProvider.overrideWithValue(FakeSessionStore()),
      sharedPreferencesProvider.overrideWithValue(preferences),
    ]);
    addTearDown(container.dispose);

    if (signedIn) {
      await container
          .read(sessionProvider.notifier)
          .adoptTokens(FakeApi.fakeTokens());
    } else {
      // The cold-start rotation, the way `main()` runs it. The gate cannot answer
      // until the session has, which is the whole reason the splash exists.
      await container.read(sessionProvider.notifier).restore();
    }

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

    return (router, container, preferences, api);
  }

  testWidgets('a fresh install opens on Get Started, with all three ways in',
      (tester) async {
    final (router, _, preferences, _) = await launch(tester);

    expect(where(router), Routes.welcome);
    expect(find.byType(WelcomeScreen), findsOneWidget);

    // Browsing, signing in and creating an account are all offered, and none of
    // them is only a word in a sentence.
    expect(find.widgetWithText(FilledButton, 'Browse as a guest'), findsOneWidget);
    expect(find.widgetWithText(OutlinedButton, 'Sign in'), findsOneWidget);
    expect(find.widgetWithText(OutlinedButton, 'Create account'), findsOneWidget);

    // Nothing is remembered until something is chosen.
    expect(preferences.getBool(chosenKey), isNull);
  });

  testWidgets('browsing as a guest opens Home and is not asked again',
      (tester) async {
    final (router, container, preferences, _) = await launch(tester);

    await tester.tap(find.widgetWithText(FilledButton, 'Browse as a guest'));
    await tester.pumpAndSettle();

    expect(where(router), Routes.search);
    expect(find.byType(WelcomeScreen), findsNothing);

    // Remembered on the DEVICE, not only in this run of the app.
    expect(preferences.getBool(chosenKey), isTrue);
    expect(container.read(entryChoiceProvider), isTrue);

    // Still a guest. Browsing is public and choosing to browse is not a sign-in.
    expect(container.read(sessionProvider).isSignedIn, isFalse);
  });

  testWidgets('a guest who has already chosen launches straight into Home',
      (tester) async {
    final (router, _, _, _) = await launch(tester, chosen: true);

    expect(where(router), Routes.search);
    expect(find.byType(WelcomeScreen), findsNothing);
  });

  testWidgets('a session on disk opens Home, never Get Started', (tester) async {
    // The choice is deliberately absent: a valid session answers the question on
    // its own, and asking a signed-in customer how they would like to use the app
    // is the app forgetting who it is talking to.
    final (router, container, _, _) =
        await launch(tester, signedIn: true, chosen: false);

    expect(container.read(sessionProvider).isSignedIn, isTrue);
    expect(where(router), Routes.search);
    expect(find.byType(WelcomeScreen), findsNothing);
  });

  testWidgets('a signed-in customer cannot land on Get Started', (tester) async {
    final (router, _, _, _) = await launch(tester, signedIn: true);

    router.go(Routes.welcome);
    await tester.pumpAndSettle();

    expect(where(router), Routes.search);
    expect(find.byType(WelcomeScreen), findsNothing);
  });

  testWidgets('a destination survives a launch that has to ask first',
      (tester) async {
    final (router, _, _, _) = await launch(tester);

    // What the router itself does when a GUARDED route is opened before the
    // session has resolved: it parks the destination on the entry point.
    router.go('${Routes.splash}?next=${Routes.documents}');
    await tester.pumpAndSettle();

    expect(find.byType(WelcomeScreen), findsOneWidget);
    expect(where(router), Routes.welcome);
    expect(at(router).queryParameters['next'], Routes.documents);

    // And it travels on to the form, so signing in answers the tap that asked.
    // Read off the SCREEN rather than the URL: Get Started pushes the form over
    // itself so the back gesture returns here, and an imperative push leaves the
    // router's location on the page underneath.
    await tester.tap(find.widgetWithText(OutlinedButton, 'Sign in'));
    await tester.pumpAndSettle();

    expect(find.byType(SignInScreen), findsOneWidget);
    expect(
      tester.widget<SignInScreen>(find.byType(SignInScreen)).next,
      Routes.documents,
    );
  });

  testWidgets('a verification link is not swallowed by Get Started',
      (tester) async {
    // The reason the gate is consulted at the entry point ONLY. A redirect across
    // every route would land this on the welcome screen with the token unspent,
    // and the link is single-use.
    final (router, _, _, api) = await launch(tester);

    router.go('${Routes.verifyEmail}?token=a-token-from-an-email');
    await tester.pumpAndSettle();

    expect(find.byType(VerifyEmailScreen), findsOneWidget);
    expect(api.verifiedTokens, ['a-token-from-an-email']);
  });

  testWidgets('signing in records the choice, so a relaunch opens Home',
      (tester) async {
    final (router, container, preferences, api) = await launch(tester);

    await tester.tap(find.widgetWithText(OutlinedButton, 'Sign in'));
    await tester.pumpAndSettle();

    // By POSITION, not by label: the design puts the label above the box as a
    // sibling rather than inside it, so there is no text in the field to find it
    // by. Email first, password second, which is the order the form is read in.
    final fields = find.byType(TextFormField);
    expect(fields, findsNWidgets(2));
    await tester.enterText(fields.at(0), 'layla@example.jo');
    await tester.enterText(fields.at(1), 'a-password');
    await tester.tap(find.widgetWithText(FilledButton, 'Sign in'));
    await tester.pumpAndSettle();

    expect(api.signInCalls, 1);
    expect(container.read(sessionProvider).isSignedIn, isTrue);
    expect(where(router), Routes.search);

    // Somebody who arrived from a link and never saw Get Started has still said
    // how they want to use the app.
    expect(preferences.getBool(chosenKey), isTrue);
  });

  testWidgets('signing in lands where the tap was aimed, not on Home',
      (tester) async {
    // The REDIRECT path, which is the one that broke. Signing in moves the session
    // to signedIn, which wakes the router and bounces `/sign-in` to `/search`,
    // tearing the form down — so anything awaited between the server's answer and
    // `context.go(next)` can let that rebuild win and silently drop the
    // destination. Reached by redirect rather than by a push, because a pushed
    // form sits on top of a page the bounce leaves alone.
    final (router, _, _, api) = await launch(tester, chosen: true);

    router.go(Routes.documents);
    await tester.pumpAndSettle();

    expect(find.byType(SignInScreen), findsOneWidget);
    expect(where(router), Routes.signIn);
    expect(at(router).queryParameters['next'], Routes.documents);

    final fields = find.byType(TextFormField);
    await tester.enterText(fields.at(0), 'layla@example.jo');
    await tester.enterText(fields.at(1), 'a-password');
    await tester.tap(find.widgetWithText(FilledButton, 'Sign in'));
    await tester.pumpAndSettle();

    expect(api.signInCalls, 1);
    expect(where(router), Routes.documents);
  });

  testWidgets('opening the sign-in form and backing out is not a choice',
      (tester) async {
    // The flag means "this device has an ANSWER". A form opened and abandoned is
    // not one, and treating it as one would take Get Started away from somebody
    // who had decided nothing.
    final (router, container, preferences, _) = await launch(tester);

    await tester.tap(find.widgetWithText(OutlinedButton, 'Sign in'));
    await tester.pumpAndSettle();
    expect(find.byType(SignInScreen), findsOneWidget);

    router.pop();
    await tester.pumpAndSettle();

    expect(find.byType(WelcomeScreen), findsOneWidget);
    expect(container.read(entryChoiceProvider), isFalse);
    expect(preferences.getBool(chosenKey), isNull);
  });

  testWidgets('Get Started is readable in Arabic, and switches into it',
      (tester) async {
    final (_, container, preferences, _) = await launch(tester);

    expect(container.read(isArabicProvider), isFalse);

    // The globe is found by what it tells a screen reader, and each language in
    // its menu is written in its own script, so the way out of the wrong language
    // does not require reading the wrong language.
    await tester.tap(find.byTooltip('Language'));
    await tester.pumpAndSettle();
    expect(find.text('English'), findsOneWidget);
    await tester.tap(find.text('العربية'));
    await tester.pumpAndSettle();

    expect(container.read(isArabicProvider), isTrue);
    expect(find.byType(WelcomeScreen), findsOneWidget);
    // Remembered, so the next launch opens in it. That the screen then turns right
    // to left is welcome_screen_test's: this harness pins the app's locale.
    expect(preferences.getString('khadra.locale'), 'ar');
  });

  testWidgets('a form opened from Get Started keeps the language switch',
      (tester) async {
    // Somebody who tapped Sign in in a language they cannot read must not have
    // to back out of the form to find the way to one they can.
    final (_, container, preferences, _) = await launch(tester);

    await tester.tap(find.widgetWithText(OutlinedButton, 'Sign in'));
    await tester.pumpAndSettle();
    expect(find.byType(SignInScreen), findsOneWidget);

    await tester.tap(find.byTooltip('Language').last);
    await tester.pumpAndSettle();
    await tester.tap(find.text('العربية'));
    await tester.pumpAndSettle();

    expect(container.read(isArabicProvider), isTrue);
    expect(find.byType(SignInScreen), findsOneWidget);
    expect(preferences.getString('khadra.locale'), 'ar');
  });
}
