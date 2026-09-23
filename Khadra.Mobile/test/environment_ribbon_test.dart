import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/core/providers.dart';
import 'package:khadra_mobile/core/widgets/environment_ribbon.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';

import 'support/fake_api.dart';

/// The strip that makes the staging build impossible to mistake for the customer app.
///
/// Two facts, two sources: "test build" is the binary's, from its flavor; "sandbox payments" is
/// the server's, and must not appear until the server has said it.
void main() {
  final en = lookupAppLocalizations(const Locale('en'));
  final ar = lookupAppLocalizations(const Locale('ar'));

  Future<void> pump(
    WidgetTester tester, {
    required bool show,
    String? paymentsMode,
    Locale locale = const Locale('en'),
  }) async {
    final api = FakeApi()..paymentsMode = paymentsMode;
    final container = ProviderContainer(overrides: [
      apiProvider.overrideWithValue(api),
      sharedPreferencesProvider.overrideWithValue(null),
    ]);
    addTearDown(container.dispose);
    await container.read(appConfigProvider.future);

    await tester.pumpWidget(
      UncontrolledProviderScope(
        container: container,
        child: MaterialApp(
          locale: locale,
          supportedLocales: AppLocalizations.supportedLocales,
          localizationsDelegates: const [
            AppLocalizations.delegate,
            GlobalMaterialLocalizations.delegate,
            GlobalWidgetsLocalizations.delegate,
            GlobalCupertinoLocalizations.delegate,
          ],
          builder: (context, child) => MediaQuery(
            data: MediaQuery.of(context).copyWith(padding: const EdgeInsets.only(top: 24)),
            child: EnvironmentRibbon(show: show, child: child!),
          ),
          home: Builder(
            builder: (context) => Text(
              'top inset ${MediaQuery.of(context).padding.top}',
              key: const ValueKey('screen'),
            ),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  final ribbon = find.byKey(const ValueKey('environment-ribbon'));

  testWidgets('production draws nothing and leaves the screen its inset', (tester) async {
    await pump(tester, show: false, paymentsMode: 'Sandbox');

    expect(ribbon, findsNothing);
    expect(find.text('top inset 24.0'), findsOneWidget);
  });

  testWidgets('staging says so before the server has said anything about money', (tester) async {
    await pump(tester, show: true, paymentsMode: 'None');

    expect(ribbon, findsOneWidget);
    expect(find.text(en.environmentStagingRibbon), findsOneWidget);
    expect(find.textContaining(en.environmentSandboxPayments), findsNothing);
  });

  testWidgets('staging adds "sandbox payments" only on the server\'s word', (tester) async {
    await pump(tester, show: true, paymentsMode: 'Sandbox');

    expect(
      find.text('${en.environmentStagingRibbon} · ${en.environmentSandboxPayments}'),
      findsOneWidget,
    );
  });

  testWidgets('it takes the status-bar inset, so the screen below draws nothing behind it',
      (tester) async {
    await pump(tester, show: true);

    expect(find.text('top inset 0.0'), findsOneWidget);
    expect(tester.getTopLeft(ribbon).dy, 0);
    expect(tester.getTopLeft(find.byKey(const ValueKey('screen'))).dy,
        greaterThanOrEqualTo(tester.getBottomLeft(ribbon).dy));
  });

  testWidgets('it reads in Arabic too, and still says TEST', (tester) async {
    await pump(tester, show: true, paymentsMode: 'Sandbox', locale: const Locale('ar'));

    expect(
      find.text('${ar.environmentStagingRibbon} · ${ar.environmentSandboxPayments}'),
      findsOneWidget,
    );
    expect(ar.environmentStagingRibbon, contains('TEST'));
  });
}
