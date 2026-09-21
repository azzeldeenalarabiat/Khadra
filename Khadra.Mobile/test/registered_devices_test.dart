import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/api/dtos.dart';
import 'package:khadra_mobile/core/providers.dart';
import 'package:khadra_mobile/core/theme/khadra_theme.dart';
import 'package:khadra_mobile/features/profile/sessions_screen.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';
import 'package:timezone/data/latest_all.dart' as tz_data;

import 'support/fake_api.dart';

/// What Registered Devices says a device IS.
///
/// It used to answer "Khadra" for every row this app had created, because the
/// app set no `User-Agent` and the screen matched the HTTP client's default —
/// `Dart/3.x (dart:io)` — on the word "dart". So a customer checking where their
/// account was signed in read the app's own name in the place the device should
/// be, on every line, and could not tell one phone from another.
void main() {
  setUpAll(tz_data.initializeTimeZones);

  final en = lookupAppLocalizations(const Locale('en'));
  final ar = lookupAppLocalizations(const Locale('ar'));

  SessionSummary session({
    required String? userAgent,
    bool isCurrent = false,
    String familyId = 'family-1',
  }) =>
      SessionSummary(
        familyId: familyId,
        signedInAt: DateTime.utc(2026, 9, 18, 9),
        lastUsedAt: DateTime.utc(2026, 9, 20, 9),
        expiresAt: DateTime.utc(2026, 10, 4, 9),
        createdByIp: '10.0.0.5',
        userAgent: userAgent,
        isActive: true,
        isCurrent: isCurrent,
      );

  Future<void> pumpDevices(
    WidgetTester tester, {
    required List<SessionSummary> sessions,
    Locale locale = const Locale('en'),
  }) async {
    tester.view.physicalSize = const Size(412, 915);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);

    final api = FakeApi()..mySessions = MySessions(sessions, 15);
    final container = ProviderContainer(overrides: [
      apiProvider.overrideWithValue(api),
      sessionStoreProvider.overrideWithValue(FakeSessionStore()),
      sharedPreferencesProvider.overrideWithValue(null),
      isArabicProvider.overrideWithValue(locale.languageCode == 'ar'),
    ]);
    addTearDown(container.dispose);

    await container.read(sessionProvider.notifier).adoptTokens(FakeApi.fakeTokens());
    await container.read(appConfigProvider.future);

    await tester.pumpWidget(
      UncontrolledProviderScope(
        container: container,
        child: MaterialApp(
          locale: locale,
          theme: KhadraTheme.light(),
          supportedLocales: AppLocalizations.supportedLocales,
          localizationsDelegates: const [
            AppLocalizations.delegate,
            GlobalMaterialLocalizations.delegate,
            GlobalWidgetsLocalizations.delegate,
            GlobalCupertinoLocalizations.delegate,
          ],
          home: const SessionsScreen(),
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('a device is never called "Khadra"', (tester) async {
    // The old behaviour, exactly: this is what an older build of the app still
    // sends, and the row it produces must now name something else.
    await pumpDevices(tester, sessions: [session(userAgent: 'Dart/3.12 (dart:io)')]);

    expect(find.text('Khadra'), findsNothing);
    expect(find.text(en.profileSessionUnknownDevice), findsOneWidget);
  });

  testWidgets('the app\'s own stamp is shown as the operating system it names',
      (tester) async {
    await pumpDevices(
      tester,
      sessions: [session(userAgent: 'Khadra (Android 14 (SDK 34))')],
    );

    expect(find.text('Android 14 (SDK 34)'), findsOneWidget);
  });

  testWidgets('the current device is marked, and only when the server says so',
      (tester) async {
    await pumpDevices(tester, sessions: [
      session(userAgent: 'Khadra (Android 14)', isCurrent: true, familyId: 'a'),
      session(userAgent: 'Khadra (iOS 18)', familyId: 'b'),
    ]);

    // One badge, on the row the server named — never two, and never one chosen
    // by the app from the timestamps.
    expect(find.text(en.profileSessionThis), findsOneWidget);
  });

  testWidgets('an older token marks nothing rather than guessing', (tester) async {
    // A token minted before the session claim existed carries no answer. The
    // honest response is an unmarked list, not a row picked by "last used" —
    // which is the last REFRESH, so another phone can outrank the one in hand.
    await pumpDevices(tester, sessions: [
      session(userAgent: 'Khadra (Android 14)', familyId: 'a'),
      session(userAgent: 'Khadra (iOS 18)', familyId: 'b'),
    ]);

    expect(find.text(en.profileSessionThis), findsNothing);
  });

  testWidgets('the action says which device it signs out, in Arabic too',
      (tester) async {
    await pumpDevices(
      tester,
      sessions: [session(userAgent: 'Khadra (Android 14)')],
      locale: const Locale('ar'),
    );

    expect(find.text(ar.profileSessionRevoke), findsOneWidget);
    // Not the bare "تسجيل الخروج", which on a list of devices says nothing about
    // WHICH one is about to be signed out.
    expect(ar.profileSessionRevoke, isNot(ar.authSignOut));
  });
}
