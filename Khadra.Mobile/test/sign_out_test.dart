import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:khadra_mobile/core/api/api_failure.dart';
import 'package:khadra_mobile/core/providers.dart';
import 'package:khadra_mobile/core/router.dart';
import 'package:khadra_mobile/core/session/session_controller.dart';
import 'package:khadra_mobile/features/profile/profile_screen.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:timezone/data/latest_all.dart' as tz_data;

import 'support/fake_api.dart';

/// Signing out, driven through the real screen.
///
/// This exists because the release web build died on the way out of the
/// confirmation dialog with a null-check failure inside Flutter's own
/// `TextEditingChannel`. Nothing in that stack was app code, and the way to tell a
/// framework fault from ours is to run the same path on a binding that is not the
/// web engine. If these pass, the sequence -- dialog, confirm, revoke, clear,
/// leave the screen -- is sound, and the fault is the engine's.
///
/// They earn their keep afterwards regardless: sign-out is the one flow where a
/// silent failure leaves somebody signed in on a device they are trying to leave.
void main() {
  setUpAll(tz_data.initializeTimeZones);

  /// The preferences the entry choice is remembered in, and the router sign-out
  /// leaves through. Both are set by `pumpProfile` and read by the tests that care,
  /// rather than widening a tuple every caller would have to unpack.
  late SharedPreferences preferences;
  late GoRouter router;

  /// What `EntryChoice` persists under. Signing out has to clear it, which is what
  /// returns the app to the unauthenticated flow on the NEXT launch as well as on
  /// this one.
  const chosenKey = 'khadra.entry_chosen';

  Future<(FakeApi, FakeSessionStore, ProviderContainer)> pumpProfile(
    WidgetTester tester, {
    Locale locale = const Locale('en'),
  }) async {
    // Tall enough that the whole profile list builds. A ListView only lays out
    // what fits, and the sign-out buttons live at the very bottom of it.
    tester.view.physicalSize = const Size(1000, 3000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);

    final api = FakeApi();
    final store = FakeSessionStore();

    // A customer who is signed in has made their choice, so the flag starts set --
    // which is the only state from which these tests can show it being cleared.
    SharedPreferences.setMockInitialValues({chosenKey: true});
    preferences = await SharedPreferences.getInstance();

    final container = ProviderContainer(overrides: [
      apiProvider.overrideWithValue(api),
      sessionStoreProvider.overrideWithValue(store),
      sharedPreferencesProvider.overrideWithValue(preferences),
    ]);
    addTearDown(container.dispose);

    // Signed in the way the app itself does it: through the token pair the server
    // hands back, so the store holds a refresh token that sign-out must revoke.
    await container
        .read(sessionProvider.notifier)
        .adoptTokens(FakeApi.fakeTokens());

    // The REAL router, not a stub. Sign-out ends by leaving the screen, and where
    // it lands is decided by the redirect that also guards every account route --
    // so a stub router would test a route table this app does not have.
    router = container.read(routerProvider);

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

    router.go(Routes.profile);
    await tester.pumpAndSettle();
    expect(find.byType(ProfileScreen), findsOneWidget);

    return (api, store, container);
  }

  /// The confirm button carries the same words as the button that opened the
  /// dialog, so it is found by position inside the dialog rather than by text.
  Future<void> confirm(WidgetTester tester) async {
    final confirmButton = find.descendant(
      of: find.byType(AlertDialog),
      matching: find.byType(FilledButton),
    );
    expect(confirmButton, findsOneWidget);
    await tester.tap(confirmButton);
    await tester.pumpAndSettle();
  }

  testWidgets('signing out revokes the family, clears the tokens and leaves',
      (tester) async {
    final (api, store, container) = await pumpProfile(tester);

    expect(container.read(sessionProvider).isSignedIn, isTrue);

    await tester.tap(find.widgetWithText(OutlinedButton, 'Sign out'));
    await tester.pumpAndSettle();
    expect(find.byType(AlertDialog), findsOneWidget);

    await confirm(tester);

    expect(tester.takeException(), isNull);
    expect(api.signOutCalls, 1);
    expect(api.signOutAllDevices, isFalse);
    expect(store.refreshToken, isNull);
    expect(store.accessToken, isNull);
    expect(container.read(sessionProvider).status, SessionStatus.signedOut);

    // Ended by a tap, not by the server. The sign-in screen must NOT accuse the
    // session of expiring.
    expect(container.read(sessionProvider).endedReason, isNull);
  });

  testWidgets('signing out returns the app to the unauthenticated flow',
      (tester) async {
    final (_, store, container) = await pumpProfile(tester);

    await tester.tap(find.widgetWithText(OutlinedButton, 'Sign out'));
    await tester.pumpAndSettle();
    await confirm(tester);

    // Get Started, not the catalogue. Somebody signing out is leaving the device or
    // switching accounts, and Get Started's Sign in IS the switch-account path --
    // the neutral thing to show on a phone that has just been handed over.
    expect(
      router.routerDelegate.currentConfiguration.uri.path,
      Routes.welcome,
    );

    // And forgotten on the DEVICE, so the next launch lands in the same place
    // rather than contradicting the screen they were left on.
    expect(store.refreshToken, isNull);
    expect(container.read(entryChoiceProvider), isFalse);
    expect(preferences.getBool(chosenKey), isNull);
  });

  testWidgets('a 401 arriving after a sign-out does not rewrite it as expired',
      (tester) async {
    final (_, store, container) = await pumpProfile(tester);

    await tester.tap(find.widgetWithText(OutlinedButton, 'Sign out'));
    await tester.pumpAndSettle();
    await confirm(tester);

    expect(container.read(sessionProvider).status, SessionStatus.signedOut);

    // The race this exists for: sign-out clears the tokens and every authenticated
    // request already in flight comes back 401 -- the alerts badge polls on a
    // one-minute loop, the bookings list is usually mid-fetch. `AuthInterceptor`
    // finds no refresh token and calls exactly this.
    await container.read(sessionProvider.notifier).endSession();
    await tester.pumpAndSettle();

    // Still a clean sign-out. The customer who had just tapped Sign out must not be
    // told on the next screen that their session ran out.
    expect(container.read(sessionProvider).status, SessionStatus.signedOut);
    expect(container.read(sessionProvider).endedReason, isNull);

    // The clearing still ran, though -- arriving here twice is not a reason to
    // leave a token behind.
    expect(store.refreshToken, isNull);
    expect(store.clearCalls, greaterThan(1));
  });

  testWidgets('cancelling the dialog changes nothing', (tester) async {
    final (api, store, container) = await pumpProfile(tester);

    await tester.tap(find.widgetWithText(OutlinedButton, 'Sign out'));
    await tester.pumpAndSettle();

    await tester.tap(find.widgetWithText(TextButton, 'Cancel'));
    await tester.pumpAndSettle();

    expect(tester.takeException(), isNull);
    expect(api.signOutCalls, 0);
    expect(store.refreshToken, isNotNull);
    expect(container.read(sessionProvider).isSignedIn, isTrue);
  });

  testWidgets('sign out everywhere asks the server to revoke every family',
      (tester) async {
    final (api, _, container) = await pumpProfile(tester);

    await tester.tap(find.widgetWithText(TextButton, 'Sign out on all devices'));
    await tester.pumpAndSettle();

    await confirm(tester);

    expect(tester.takeException(), isNull);
    expect(api.signOutCalls, 1);
    expect(api.signOutAllDevices, isTrue);
    expect(container.read(sessionProvider).status, SessionStatus.signedOut);
  });

  testWidgets('an unreachable server still ends the session on this device',
      (tester) async {
    final (api, store, container) = await pumpProfile(tester);
    api.signOutFailure = const ApiFailure(kind: ApiFailureKind.offline);

    await tester.tap(find.widgetWithText(OutlinedButton, 'Sign out'));
    await tester.pumpAndSettle();
    await confirm(tester);

    // The server keeps the family alive for its fourteen days; refusing to sign
    // out locally over that would strand somebody on a device they are leaving.
    expect(tester.takeException(), isNull);
    expect(store.refreshToken, isNull);
    expect(container.read(sessionProvider).status, SessionStatus.signedOut);
  });

  testWidgets('the same path in Arabic', (tester) async {
    final (api, store, container) =
        await pumpProfile(tester, locale: const Locale('ar'));

    // Found by type and position rather than by the Arabic string, so the test
    // does not break the day a translation is reworded.
    await tester.tap(find.byType(OutlinedButton).last);
    await tester.pumpAndSettle();
    expect(find.byType(AlertDialog), findsOneWidget);

    await confirm(tester);

    expect(tester.takeException(), isNull);
    expect(api.signOutCalls, 1);
    expect(store.refreshToken, isNull);
    expect(container.read(sessionProvider).status, SessionStatus.signedOut);
  });
}
