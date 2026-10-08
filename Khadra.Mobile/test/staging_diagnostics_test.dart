import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/core/diagnostics/staging_diagnostics.dart';
import 'package:khadra_mobile/core/providers.dart';
import 'package:khadra_mobile/core/push/push_trace.dart';
import 'package:khadra_mobile/core/router.dart';
import 'package:khadra_mobile/features/profile/profile_screen.dart';
import 'package:khadra_mobile/features/profile/push_trace_screen.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:timezone/data/latest_all.dart' as tz_data;

import 'support/fake_api.dart';

/// TEMPORARY (W4-9): the Staging build marker and the way into the push trace.
///
/// On the phone, a Staging build showed no diagnostics at all. Whatever the reason, they
/// must not depend on ONE signal: they are on when the Dart flavor OR the Android
/// application id says Staging, they say so when the two disagree, and they sit at the
/// top of Profile, signed in or not. (`flutter test` builds the default flavor, production, so every case
/// here has the Dart side saying "production" — exactly the case a wrong flavor would be.)
void main() {
  setUpAll(tz_data.initializeTimeZones);

  tearDown(() {
    StagingDiagnostics.attach(packageName: null, version: null, buildNumber: null);
    PushTrace.enabled = false;
  });

  group('which builds show them', () {
    test('a Staging application id turns them on even when the flavor says otherwise, and says so', () {
      StagingDiagnostics.attach(packageName: 'com.khadra.khadra_mobile.staging', version: '1.3.0', buildNumber: '6');

      expect(StagingDiagnostics.flavorSaysStaging, isFalse);
      expect(StagingDiagnostics.enabled, isTrue);
      expect(StagingDiagnostics.mismatch, isTrue);
      expect(StagingDiagnostics.headline, 'staging diagnostics · $diagnosticsMarker · 1.3.0+6 · FLAVOR MISMATCH');
      expect(StagingDiagnostics.facts(), contains('package=com.khadra.khadra_mobile.staging'));
      expect(StagingDiagnostics.facts(), contains('appFlavor=production resolved=production'));
    });

    test('the production application id shows nothing', () {
      StagingDiagnostics.attach(packageName: 'com.khadra.khadra_mobile', version: '1.3.0', buildNumber: '6');

      expect(StagingDiagnostics.enabled, isFalse);
      expect(StagingDiagnostics.mismatch, isFalse);
    });

    test('a build that could not read its package info is not called a mismatch', () {
      expect(StagingDiagnostics.enabled, isFalse);
      expect(StagingDiagnostics.mismatch, isFalse);
    });
  });

  Future<void> pumpProfile(WidgetTester tester, {required bool signedIn}) async {
    tester.view.physicalSize = const Size(1000, 3000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    SharedPreferences.setMockInitialValues({
      'khadra.entry_chosen': true,
      // What MainActivity writes (Kotlin, `PushIntentTrace`): one string of lines.
      'khadra.push_trace.native': '10:00:00.000 native-new-intent action=android.intent.action.MAIN fcm=true '
          'local=false history=false keys=[google.message_id,kind,notificationId] kind=YourDocumentRejected',
    });
    final preferences = await SharedPreferences.getInstance();
    final container = ProviderContainer(overrides: [
      apiProvider.overrideWithValue(FakeApi()),
      sessionStoreProvider.overrideWithValue(FakeSessionStore()),
      sharedPreferencesProvider.overrideWithValue(preferences),
    ]);
    addTearDown(container.dispose);
    if (signedIn) {
      await container.read(sessionProvider.notifier).adoptTokens(FakeApi.fakeTokens());
    } else {
      await container.read(sessionProvider.notifier).restore();
    }
    final router = container.read(routerProvider);
    await tester.pumpWidget(UncontrolledProviderScope(
      container: container,
      child: MaterialApp.router(
        routerConfig: router,
        supportedLocales: AppLocalizations.supportedLocales,
        localizationsDelegates: const [
          AppLocalizations.delegate,
          GlobalMaterialLocalizations.delegate,
          GlobalWidgetsLocalizations.delegate,
          GlobalCupertinoLocalizations.delegate,
        ],
      ),
    ));
    await tester.pumpAndSettle();
    router.go(Routes.profile);
    await tester.pumpAndSettle();
    expect(find.byType(ProfileScreen), findsOneWidget);
  }

  for (final signedIn in [true, false]) {
    testWidgets('a Staging build shows its marker FIRST on Profile (${signedIn ? 'signed in' : 'guest'}), and it opens the trace',
        (tester) async {
      StagingDiagnostics.attach(packageName: 'com.khadra.khadra_mobile.staging', version: '1.3.0', buildNumber: '6');
      PushTrace.enabled = StagingDiagnostics.enabled;
      await pumpProfile(tester, signedIn: signedIn);

      final strip = find.textContaining('staging diagnostics · $diagnosticsMarker · 1.3.0+6');
      expect(strip, findsOneWidget);
      expect(find.text('push trace (staging)'), findsOneWidget);
      // First on the screen: above everything else Profile shows.
      expect(tester.getTopLeft(strip).dy, lessThan(200));

      await tester.tap(strip);
      await tester.pumpAndSettle();

      expect(find.byType(PushTraceScreen), findsOneWidget);
      final report = tester.widget<SelectableText>(find.byType(SelectableText)).data!;
      expect(report, contains('marker=$diagnosticsMarker'));
      expect(report, contains('package=com.khadra.khadra_mobile.staging'));
      expect(report, contains('# android (MainActivity intents)'));
      expect(report, contains('native-new-intent action=android.intent.action.MAIN fcm=true'));
    });
  }

  testWidgets('a production build shows neither the marker nor the trace', (tester) async {
    StagingDiagnostics.attach(packageName: 'com.khadra.khadra_mobile', version: '1.3.0', buildNumber: '6');
    PushTrace.enabled = StagingDiagnostics.enabled;
    await pumpProfile(tester, signedIn: true);

    expect(find.textContaining('staging diagnostics'), findsNothing);
    expect(find.text('push trace (staging)'), findsNothing);
  });
}
