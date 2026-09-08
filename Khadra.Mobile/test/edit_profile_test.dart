import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/api/dtos.dart';
import 'package:khadra_mobile/core/api/api_failure.dart';
import 'package:khadra_mobile/core/providers.dart';
import 'package:khadra_mobile/core/router.dart';
import 'package:khadra_mobile/features/notifications/notification_providers.dart';
import 'package:khadra_mobile/features/profile/edit_profile_screen.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';
import 'package:timezone/data/latest_all.dart' as tz_data;

import 'support/fake_api.dart';

/// Editing a profile, driven through the real screen.
///
/// Written because the browser harness cannot reach Flutter web's text input: it
/// can tap and read, but typing needs a trusted keyboard event the harness has no
/// way to produce, so a run that LOOKED like it saved a new name in fact saved
/// nothing. Here `enterText` goes through the framework's own editing pipeline,
/// which is the same one a keyboard drives.
void main() {
  setUpAll(tz_data.initializeTimeZones);

  Future<(FakeApi, ProviderContainer)> pumpEditProfile(WidgetTester tester) async {
    tester.view.physicalSize = const Size(1000, 2400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);

    final api = FakeApi();
    final container = ProviderContainer(overrides: [
      apiProvider.overrideWithValue(api),
      sessionStoreProvider.overrideWithValue(FakeSessionStore()),
      sharedPreferencesProvider.overrideWithValue(null),
      unreadNotificationCountProvider.overrideWith((ref) => Stream.value(0)),
    ]);
    addTearDown(container.dispose);

    await container
        .read(sessionProvider.notifier)
        .adoptTokens(FakeApi.fakeTokens());

    final router = container.read(routerProvider);

    await tester.pumpWidget(
      UncontrolledProviderScope(
        container: container,
        child: MaterialApp.router(
          locale: const Locale('en'),
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

    router.go(Routes.editProfile);
    await tester.pumpAndSettle();
    expect(find.byType(EditProfileScreen), findsOneWidget);

    return (api, container);
  }

  Finder fieldLabelled(String label) => find.ancestor(
        of: find.text(label),
        matching: find.byType(TextFormField),
      );

  testWidgets('opens holding what the account already says', (tester) async {
    await pumpEditProfile(tester);

    expect(find.widgetWithText(TextFormField, 'Layla Odeh'), findsOneWidget);
    expect(find.widgetWithText(TextFormField, '+962791234567'), findsOneWidget);

    // The address is shown, and the reason it cannot be edited is shown with it.
    expect(find.text('layla@example.jo'), findsOneWidget);
    expect(find.byType(TextFormField), findsNWidgets(2));
  });

  testWidgets('sends the typed name and phone, and adopts what comes back',
      (tester) async {
    final (api, container) = await pumpEditProfile(tester);

    // The server normalises 07… to +9627…. The screen must show the SERVER's
    // version afterwards, not the digits that were typed.
    api.onUpdateProfile = (name, phone) => AuthUser(
          id: FakeApi.fakeUser().id,
          email: FakeApi.fakeUser().email,
          fullName: name,
          phone: '+962791629748',
          role: 'Customer',
          isEmailVerified: true,
          mustChangePassword: false,
          createdAt: FakeApi.fakeUser().createdAt,
        );

    await tester.enterText(
        find.widgetWithText(TextFormField, 'Layla Odeh'), 'Layla Odeh Al-Rashid');
    await tester.enterText(
        find.widgetWithText(TextFormField, '+962791234567'), '0791629748');
    await tester.pump();

    await tester.tap(find.widgetWithText(FilledButton, 'Save changes'));
    await tester.pumpAndSettle();

    expect(api.updatedName, 'Layla Odeh Al-Rashid');
    expect(api.updatedPhone, '0791629748');

    final user = container.read(sessionProvider).user!;
    expect(user.fullName, 'Layla Odeh Al-Rashid');
    expect(user.phone, '+962791629748');

    // Saving leaves the screen, back to the profile it came from.
    expect(find.byType(EditProfileScreen), findsNothing);
  });

  testWidgets('an empty name is refused before a request is made',
      (tester) async {
    final (api, _) = await pumpEditProfile(tester);

    await tester.enterText(
        find.widgetWithText(TextFormField, 'Layla Odeh'), '   ');
    await tester.tap(find.widgetWithText(FilledButton, 'Save changes'));
    await tester.pumpAndSettle();

    expect(api.updatedName, isNull);
    expect(find.byType(EditProfileScreen), findsOneWidget);
  });

  testWidgets('a phone that is not a Jordanian mobile is refused locally',
      (tester) async {
    final (api, _) = await pumpEditProfile(tester);

    await tester.enterText(
        find.widgetWithText(TextFormField, '+962791234567'), '12345');
    await tester.tap(find.widgetWithText(FilledButton, 'Save changes'));
    await tester.pumpAndSettle();

    expect(api.updatedName, isNull);
    expect(find.byType(EditProfileScreen), findsOneWidget);
  });

  testWidgets('a number already on another account is reported, not swallowed',
      (tester) async {
    final (api, container) = await pumpEditProfile(tester);
    api.updateProfileFailure = const ApiFailure(
      kind: ApiFailureKind.conflict,
      code: 'user.phone_already_used',
      statusCode: 409,
    );

    await tester.enterText(
        find.widgetWithText(TextFormField, '+962791234567'), '0799999999');
    await tester.tap(find.widgetWithText(FilledButton, 'Save changes'));
    await tester.pumpAndSettle();

    // Still on the screen, with the message and the typed value intact so the
    // correction is one edit away rather than a re-entry.
    expect(find.byType(EditProfileScreen), findsOneWidget);
    expect(find.widgetWithText(TextFormField, '0799999999'), findsOneWidget);
    expect(container.read(sessionProvider).user!.phone, '+962791234567');
  });

  testWidgets('the field labels are the localised ones', (tester) async {
    await pumpEditProfile(tester);
    expect(fieldLabelled('Full name'), findsOneWidget);
    expect(fieldLabelled('Phone number'), findsOneWidget);
  });
}
