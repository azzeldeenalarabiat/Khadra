import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/core/config/build_stamp.dart';
import 'package:khadra_mobile/core/providers.dart';
import 'package:khadra_mobile/core/theme/khadra_theme.dart';
import 'package:khadra_mobile/core/widgets/khadra_widgets.dart';
import 'package:khadra_mobile/features/profile/profile_screen.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';

import 'support/fake_api.dart';

/// Which build a phone is running (pre-launch item 244): the version the platform reports,
/// and the commit stamped into the compiled Dart at build time.
void main() {
  const commit = '76ea7bc6c9252b289f72c296d490f08cff8b372d';

  group('the line', () {
    test('is the version and the first twelve characters of the commit', () {
      expect(buildStampLine(installedVersion: '1.4.0+8', commit: commit), '1.4.0+8 · 76ea7bc6c925');
    });

    test('is the version alone for a build nobody stamped', () {
      expect(buildStampLine(installedVersion: '1.4.0+8', commit: ''), '1.4.0+8');
    });

    test('is the commit alone when the platform would not say the version', () {
      expect(buildStampLine(installedVersion: null, commit: commit), '76ea7bc6c925');
      expect(buildStampLine(installedVersion: '  ', commit: commit), '76ea7bc6c925');
    });

    test('shows a short commit whole', () {
      expect(buildStampLine(installedVersion: '1.4.0+8', commit: '76ea7bc'), '1.4.0+8 · 76ea7bc');
    });

    test('leaves off anything that is not a commit, rather than show it', () {
      for (final notACommit in ['main', 'HEAD', '76EA7BC6C925', '76ea7bc 6c925', r'$(git rev-parse HEAD)', '76ea7b']) {
        expect(
          buildStampLine(installedVersion: '1.4.0+8', commit: notACommit),
          '1.4.0+8',
          reason: notACommit,
        );
      }
    });

    test('is nothing at all when there is nothing to show', () {
      expect(buildStampLine(installedVersion: null, commit: ''), isNull);
    });

    test('an unstamped run (flutter run, the tests) invents no commit', () {
      expect(buildCommit, isEmpty);
    });
  });

  group('Profile', () {
    Future<void> pumpProfile(WidgetTester tester, {required Locale locale, required String commit}) async {
      tester.view.physicalSize = const Size(412, 1400);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);

      final container = ProviderContainer(
        overrides: [
          apiProvider.overrideWithValue(FakeApi()),
          sessionStoreProvider.overrideWithValue(FakeSessionStore()),
          sharedPreferencesProvider.overrideWithValue(null),
          isArabicProvider.overrideWithValue(locale.languageCode == 'ar'),
          installedAppVersionProvider.overrideWithValue('1.4.0+8'),
          buildCommitProvider.overrideWithValue(commit),
        ],
      );
      addTearDown(container.dispose);

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
            home: const ProfileScreen(),
          ),
        ),
      );
      await tester.pumpAndSettle();
    }

    testWidgets('shows the build at its foot, to a guest as well', (tester) async {
      await pumpProfile(tester, locale: const Locale('en'), commit: commit);

      final line = find.text('1.4.0+8 · 76ea7bc6c925');
      await tester.scrollUntilVisible(line, 200, scrollable: find.byType(Scrollable).first);
      expect(line, findsOneWidget);
    });

    testWidgets('keeps it left to right in Arabic, so the version and commit are not reordered', (tester) async {
      await pumpProfile(tester, locale: const Locale('ar'), commit: commit);

      final line = find.text('1.4.0+8 · 76ea7bc6c925');
      await tester.scrollUntilVisible(line, 200, scrollable: find.byType(Scrollable).first);
      expect(find.ancestor(of: line, matching: find.byType(LatinRun)), findsOneWidget);
      expect(Directionality.of(tester.element(line)), TextDirection.ltr);
    });
  });
}
